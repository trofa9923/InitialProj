using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;

namespace Microsoft.ProjectOxford.Face.Controls
{
    /// <summary>
    /// Interaction logic for FaceVerification.xaml
    /// </summary>
    public partial class FaceVerificationPage : Page, INotifyPropertyChanged
    {
        #region Fields

        /// <summary>
        /// Description dependency property
        /// </summary>
        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register("Description", typeof(string), typeof(FaceVerificationPage));

        /// <summary>
        /// RecognitionModel for Face detection and LargePersonGroup
        /// </summary>
        private static readonly string recognitionModel = RecognitionModel.Recognition02;

        /// <summary>
        /// DetectionModel for Face detection
        /// </summary>
        private static readonly string detectionModel = DetectionModel.Detection02;

        /// <summary>
        /// Temporary group name for create person database
        /// </summary>
        public static readonly string SampleGroupName = Guid.NewGuid().ToString();

        /// <summary>
        /// Face detection result container for image on the left
        /// </summary>
        private ObservableCollection<Face> _leftResultCollection = new ObservableCollection<Face>();

        /// <summary>
        /// Face detection result container for image on the right
        /// </summary>
        private ObservableCollection<Face> _rightResultCollection = new ObservableCollection<Face>();

        /// <summary>
        /// Face detected for face to person verification
        /// </summary>
        private ObservableCollection<Face> _rightFaceResultCollection = new ObservableCollection<Face>();
        
        /// <summary>
        /// Faces collection which contains all faces of the person
        /// </summary>
        private ObservableCollection<Face> _facesCollection = new ObservableCollection<Face>();

        /// <summary>
        /// Face to face verification result
        /// </summary>
        private string _faceVerifyResult;

        /// <summary>
        /// Face to person verification result
        /// </summary>
        private string _personVerifyResult;
        
        /// <summary>
        /// max concurrent process number for client query.a
        /// </summary>
        private int _maxConcurrentProcesses;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="FaceVerificationPage" /> class
        /// </summary>
        public FaceVerificationPage()
        {
            InitializeComponent();
            _maxConcurrentProcesses = 4;
        }

        #endregion Constructors

        #region Events

        /// <summary>
        /// Implement INotifyPropertyChanged interface
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion Events

        #region Properties

        /// <summary>
        /// Gets or sets description for UI rendering
        /// </summary>
        public string Description
        {
            get
            {
                return (string)GetValue(DescriptionProperty);
            }

            set
            {
                SetValue(DescriptionProperty, value);
            }
        }
        
        /// <summary>
        /// Gets face detection results for image on the left
        /// </summary>
        public ObservableCollection<Face> LeftResultCollection
        {
            get
            {
                return _leftResultCollection;
            }
        }

        /// <summary>
        /// Gets max image size for UI rendering
        /// </summary>
        public int MaxImageSize
        {
            get
            {
                return 300;
            }
        }

        /// <summary>
        /// Gets face detection results for image on the right
        /// </summary>
        public ObservableCollection<Face> RightResultCollection
        {
            get
            {
                return _rightResultCollection;
            }
        }

        /// <summary>
        /// Gets face detection results for face to person verification
        /// </summary>
        public ObservableCollection<Face> RightFaceResultCollection
        {
            get
            {
                return _rightFaceResultCollection;
            }
        }

        /// <summary>
        /// Gets faces of the person
        /// </summary>
        public ObservableCollection<Face> FacesCollection
        {
            get
            {
                return _facesCollection;
            }
        }

        /// <summary>
        /// Gets or sets selected face verification result
        /// </summary>
        public string FaceVerifyResult
        {
            get
            {
                return _faceVerifyResult;
            }

            set
            {
                _faceVerifyResult = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("FaceVerifyResult"));
                }
            }
        }

        /// <summary>
        /// Gets or sets selected face person verification result
        /// </summary>
        public string PersonVerifyResult
        {
            get
            {
                return _personVerifyResult;
            }

            set
            {
                _personVerifyResult = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("PersonVerifyResult"));
                }
            }
        }

        /// <summary>
        /// Gets group name
        /// </summary>
        public string GroupName
        {
            get
            {
                return SampleGroupName;
            }
        }

        /// <summary>
        /// Person for verification
        /// </summary>
        public Person Person { get; set; } = new Person();

        #endregion Properties

        #region Methods

        /// <summary>
        /// Pick image for detection, get detection result and put detection results into LeftResultCollection 
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event argument</param>
        private async void LeftImagePicker_Click(object sender, RoutedEventArgs e)
        {
            // Show image picker, show jpg type files only
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".jpg";
            dlg.Filter = "Image files(*.jpg, *png, *.bmp, *.gif) | *.jpg; *.png; *.bmp; *.gif";
            var result = dlg.ShowDialog();

            if (result.HasValue && result.Value)
            {
                FaceVerifyResult = string.Empty;

                // User already picked one image
                var pickedImagePath = dlg.FileName;
                var renderingImage = UIHelper.LoadImageAppliedOrientation(pickedImagePath);
                var imageInfo = UIHelper.GetImageInfoForRendering(renderingImage);
                LeftImageDisplay.Source = renderingImage;

                // Clear last time detection results
                LeftResultCollection.Clear();
                FaceVerifyButton.IsEnabled = (LeftResultCollection.Count!=0 && RightResultCollection.Count!=0);
                MainWindow.Log("Request: Detecting in {0}", pickedImagePath);
                var sw = Stopwatch.StartNew();

                // Call detection REST API, detect faces inside the image
                using (var fileStream = File.OpenRead(pickedImagePath))
                {
                    try
                    {
                        var faceServiceClient = FaceServiceClientHelper.GetInstance(this);
                        var faces = await faceServiceClient.Face.DetectWithStreamAsync(fileStream, recognitionModel: recognitionModel, detectionModel: detectionModel);

                        // Handle REST API calling error
                        if (faces == null)
                        {
                            return;
                        }

                        MainWindow.Log("Response: Success. Detected {0} face(s) in {1}", faces.Count, pickedImagePath);

                        // Convert detection results into UI binding object for rendering
                        foreach (var face in UIHelper.CalculateFaceRectangleForRendering(faces, MaxImageSize, imageInfo))
                        {
                            // Detected faces are hosted in result container, will be used in the verification later
                            LeftResultCollection.Add(face);
                        }

                        FaceVerifyButton.IsEnabled = (LeftResultCollection.Count != 0 && RightResultCollection.Count != 0);
                    }
                    catch (APIErrorException ex)
                    {
                        MainWindow.Log("Response: {0}. {1}", ex.Body.Error.Code, ex.Body.Error.Message);
                    }
                }
            }
            GC.Collect();
        }

        /// <summary>
        /// Pick image for detection, get detection result and put detection results into RightResultCollection 
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event argument</param>
        private async void RightImagePicker_Click(object sender, RoutedEventArgs e)
        {
            // Show image picker, show jpg type files only
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".jpg";
            dlg.Filter = "Image files(*.jpg, *.png, *.bmp, *.gif) | *.jpg; *.png; *.bmp; *.gif";
            var result = dlg.ShowDialog();

            if (result.HasValue && result.Value)
            {
                FaceVerifyResult = string.Empty;

                // User already picked one image
                var pickedImagePath = dlg.FileName;
                var renderingImage = UIHelper.LoadImageAppliedOrientation(pickedImagePath);
                var imageInfo = UIHelper.GetImageInfoForRendering(renderingImage);
                RightImageDisplay.Source = renderingImage;

                // Clear last time detection results
                RightResultCollection.Clear();
                FaceVerifyButton.IsEnabled = (LeftResultCollection.Count != 0 && RightResultCollection.Count != 0);

                MainWindow.Log("Request: Detecting in {0}", pickedImagePath);
                var sw = Stopwatch.StartNew();

                // Call detection REST API, detect faces inside the image
                using (var fileStream = File.OpenRead(pickedImagePath))
                {
                    try
                    {
                        var faceServiceClient = FaceServiceClientHelper.GetInstance(this);
                        var faces = await faceServiceClient.Face.DetectWithStreamAsync(fileStream, recognitionModel: recognitionModel, detectionModel: detectionModel);

                        // Handle REST API calling error
                        if (faces == null)
                        {
                            return;
                        }

                        MainWindow.Log("Response: Success. Detected {0} face(s) in {1}", faces.Count, pickedImagePath);

                        // Convert detection results into UI binding object for rendering
                        foreach (var face in UIHelper.CalculateFaceRectangleForRendering(faces, MaxImageSize, imageInfo))
                        {
                            // Detected faces are hosted in result container, will be used in the verification later
                            RightResultCollection.Add(face);
                        }
                        FaceVerifyButton.IsEnabled = (LeftResultCollection.Count != 0 && RightResultCollection.Count != 0);
                    }
                    catch (APIErrorException ex)
                    {
                        MainWindow.Log("Response: {0}. {1}", ex.Body.Error.Code, ex.Body.Error.Message);

                        return;
                    }
                }
            }
            GC.Collect();
        }

        /// <summary>
        /// Verify two detected faces, get whether these two faces belong to the same person
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event argument</param>
        private async void Face2FaceVerification_Click(object sender, RoutedEventArgs e)
        {
            // Call face to face verification, verify REST API supports one face to one face verification only
            // Here, we handle single face image only
            if (LeftResultCollection.Count == 1 && RightResultCollection.Count == 1)
            {
                FaceVerifyResult = "Verifying...";
                var faceId1 = LeftResultCollection[0].FaceId;
                var faceId2 = RightResultCollection[0].FaceId;

                MainWindow.Log("Request: Verifying face {0} and {1}", faceId1, faceId2);

                // Call verify REST API with two face id
                try
                {
                    var faceServiceClient = FaceServiceClientHelper.GetInstance(this);
                    var res = await faceServiceClient.Face.VerifyFaceToFaceAsync(Guid.Parse(faceId1), Guid.Parse(faceId2));

                    // Verification result contains IsIdentical (true or false) and Confidence (in range 0.0 ~ 1.0),
                    // here we update verify result on UI by FaceVerifyResult binding
                    FaceVerifyResult = string.Format("Confidence = {0:0.00}, {1}", res.Confidence,  res.IsIdentical ? "two faces belong to same person" : "two faces not belong to same person");
                    MainWindow.Log("Response: Success. Face {0} and {1} {2} to the same person", faceId1, faceId2, res.IsIdentical ? "belong" : "not belong");
                }
                catch (APIErrorException ex)
                {
                    MainWindow.Log("Response: {0}. {1}", ex.Body.Error.Code, ex.Body.Error.Message);

                    return;
                }
            }
            else
            {
                MessageBox.Show("Verification accepts two faces as input, please pick images with only one detectable face in it.", "Warning", MessageBoxButton.OK);
            }
            GC.Collect();
        }

        #endregion Methods

        private void RichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }

    /// <summary>
    /// Person structure for UI binding
    /// </summary>
    public class Person : INotifyPropertyChanged
    {
        #region Fields

        /// <summary>
        /// Person's faces from database
        /// </summary>
        private ObservableCollection<Face> _faces = new ObservableCollection<Face>();

        /// <summary>
        /// Person's id
        /// </summary>
        private string _personId;

        /// <summary>
        /// Person's name
        /// </summary>
        private string _personName;

        #endregion Fields

        #region Events

        /// <summary>
        /// Implement INotifyPropertyChanged interface
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion Events

        #region Properties

        /// <summary>
        /// Gets or sets person's faces from database
        /// </summary>
        public ObservableCollection<Face> Faces
        {
            get
            {
                return _faces;
            }

            set
            {
                _faces = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("Faces"));
                }
            }
        }

        /// <summary>
        /// Gets or sets person's id
        /// </summary>
        public string PersonId
        {
            get
            {
                return _personId;
            }

            set
            {
                _personId = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("PersonId"));
                }
            }
        }

        /// <summary>
        /// Gets or sets person's name
        /// </summary>
        public string PersonName
        {
            get
            {
                return _personName;
            }

            set
            {
                _personName = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("PersonName"));
                }
            }
        }

        #endregion Properties         
    }
}