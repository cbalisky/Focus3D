using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Threading.Tasks;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Focus3D.Resources;
using Microsoft.Devices;
using System.IO;
using System.IO.IsolatedStorage;
using Microsoft.Xna.Framework.Media;
using Windows.Phone.Media.Capture;
using System.Windows.Media.Imaging;
using System.ComponentModel;

namespace Focus3D
{
    public partial class MainPage : PhoneApplicationPage
    {
        private int savedCounter = 0;
        MediaLibrary library = new MediaLibrary();
        PhotoCaptureDevice camManual;
        private int focusRange = 0;
        private int threshold = 1;
        CameraFocusStatus s = CameraFocusStatus.Locked;
        private WriteableBitmap wb;
        private int[] depthBuffer;
        private int[] _sharpnessArea;
        private int[] _sharpnessMap;
        private FocusMap bufferMapNew, bufferMapOld;
        private Windows.Foundation.Size _previewFrameSize = new Windows.Foundation.Size();
        private CaptureMode mode = CaptureMode.DepthMap;
        public FilterMethod FilterMode = FilterMethod.Salt;
        public int FilterSize = 1;
        private bool _running = false;
        public bool Running
        {
            get
            {
                return _running;
            }
        }
        public enum FocusMethod { Variance, Sobel };
        public enum CaptureMode { DepthMap, FocusSweepSave };
        public enum FilterMethod { None, Median, Salt, Interpolation };
        // Constructor
        public MainPage()
        {
            
            InitializeComponent();

            // Sample code to localize the ApplicationBar
            BuildLocalizedApplicationBar();
        }

        // Sample code for building a localized ApplicationBar
        private void BuildLocalizedApplicationBar()
        {
            // Set the page's ApplicationBar to a new instance of ApplicationBar.
            ApplicationBar = new ApplicationBar();
            ApplicationBar.StateChanged += ApplicationBar_StateChanged;
            // Create a new button and set the text value to the localized string from AppResources.
            ApplicationBarIconButton appBarButton = new ApplicationBarIconButton(new Uri("/Assets/AppBar/appbar.add.rest.png", UriKind.Relative));
            appBarButton.Text = AppResources.AppBarButtonText;
            //ApplicationBar.Buttons.Add(appBarButton);
            ApplicationBar.Mode = ApplicationBarMode.Minimized;
            ApplicationBar.BackgroundColor = Color.FromArgb(0,64, 64, 64);
            // Create a new menu item with the localized string from AppResources.
            ApplicationBarMenuItem appBarMenuItem = new ApplicationBarMenuItem(AppResources.AppBarMenuItemText);
            appBarMenuItem.Text = "Switch to Focus Sweep Save";
            ApplicationBar.MenuItems.Add(appBarMenuItem);
            appBarMenuItem.Click += appBarMenuItem_Click;

            ApplicationBarMenuItem ChooseFilter = new ApplicationBarMenuItem(AppResources.AppBarMenuItemText);
            ChooseFilter.Text = "Choose Post Process";
            ApplicationBar.MenuItems.Add(ChooseFilter);
            ChooseFilter.Click += ChooseFilter_Click;

            ApplicationBarMenuItem ChooseFilterSize = new ApplicationBarMenuItem(AppResources.AppBarMenuItemText);
            ChooseFilterSize.Text = "Change Filter Size";
            ApplicationBar.MenuItems.Add(ChooseFilterSize);

            ChooseFilterSize.Click += ChooseFilterSize_Click;
            FilterSizeTextBox.GotFocus += FilterSizeTextBox_GotFocus;
            FilterSizeTextBox.LostFocus +=FilterSizeTextBox_LostFocus;
            FilterChoice.LostFocus += FilterChoice_LostFocus;
        }


        private async void InitializeCamera(CameraSensorLocation sensorLocation)
        {
            IReadOnlyList<Windows.Foundation.Size> res = PhotoCaptureDevice.GetAvailablePreviewResolutions(CameraSensorLocation.Back);
            // Open camera device
            if (camManual == null)
                camManual = await PhotoCaptureDevice.OpenAsync(sensorLocation, new Windows.Foundation.Size(640, 480));
            //Set the VideoBrush source to the camera.
            viewfinderBrush.SetSource(camManual);

            _previewFrameSize = camManual.PreviewResolution;
            int w = (int)camManual.PreviewResolution.Width;
            int h = (int)camManual.PreviewResolution.Height;
            // Display camera viewfinder data in XAML videoBrush element
            

            //Set Depth Buffer to Black
            depthBuffer = new int[w * h];
            /*int black = BitConverter.ToInt32(new byte[] { 0, 0, 0, 255 }, 0);
            for (int i = 0; i < depthBuffer.Length; i++)
                depthBuffer[i] = black;
            */
            CameraButtons.ShutterKeyPressed += CameraButtons_ShutterKeyPressed;
            if (camManual != null)
            {
                // LandscapeRight rotation when camera is on back of phone.
                int landscapeRightRotation = 180;

                // Rotate video brush from camera.
                if (this.Orientation == PageOrientation.LandscapeRight)
                {
                    // Rotate for LandscapeRight orientation.
                    viewfinderBrush.RelativeTransform =
                        new CompositeTransform() { CenterX = 0.5, CenterY = 0.5, Rotation = landscapeRightRotation };
                }
                else
                {
                    // Rotate for standard landscape orientation.
                    viewfinderBrush.RelativeTransform =
                        new CompositeTransform() { CenterX = 0.5, CenterY = 0.5, Rotation = 0 };
                }
            }
        }

        void CameraButtons_ShutterKeyPressed(object sender, EventArgs e)
        {
            Capture_Sequence();
        }


        protected override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
        {

            // Check to see if the camera is available on the phone.
            if ((PhotoCamera.IsCameraTypeSupported(CameraType.Primary) == true) ||
                 (PhotoCamera.IsCameraTypeSupported(CameraType.FrontFacing) == true))
            {
                // Initialize the camera, when available.
                if (PhotoCamera.IsCameraTypeSupported(CameraType.Primary))
                {
                    // Use front-facing camera if available.
                   //cam = new Microsoft.Devices.PhotoCamera(CameraType.Primary);
                    InitializeCamera(CameraSensorLocation.Back);

                    _sharpnessArea = new int[] { 1, 1 };
                }
                else
                {
                    // Otherwise, use standard camera on back of phone.
                    
                }
            }
            else
            {
                // The camera is not supported on the phone.
                this.Dispatcher.BeginInvoke(delegate()
                {
                    // Write message.
                    txtDebug.Text = "A Camera is not available on this phone.";
                });

                // Disable UI.
                ShutterButton.IsEnabled = false;
            }
        }
        protected override void OnNavigatingFrom(System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            
            if (!_running)
           {
                if (camManual != null)
                {
                    // Dispose camera to minimize power consumption and to expedite shutdown.
                    camManual.Dispose();
                    // Release memory, ensure garbage collection.
                }
                wb = null;
                depthBuffer = null;
            }
            
        }

        // Update the UI if initialization succeeds.
        void cam_Initialized(object sender, Microsoft.Devices.CameraOperationCompletedEventArgs e)
        {
            if (e.Succeeded)
            {
                this.Dispatcher.BeginInvoke(delegate()
                {
                    // Write message.
                    txtDebug.Text = "Camera initialized.";
                });
            }
        }

        // Ensure that the viewfinder is upright in LandscapeRight.
        protected override void OnOrientationChanged(OrientationChangedEventArgs e)
        {
            if (camManual != null)
            {
                // LandscapeRight rotation when camera is on back of phone.
                int landscapeRightRotation = 180;

                // Rotate video brush from camera.
                if (e.Orientation == PageOrientation.LandscapeRight)
                {
                    // Rotate for LandscapeRight orientation.
                    viewfinderBrush.RelativeTransform =
                        new CompositeTransform() { CenterX = 0.5, CenterY = 0.5, Rotation = landscapeRightRotation };
                }
                else
                {
                    // Rotate for standard landscape orientation.
                    viewfinderBrush.RelativeTransform =
                        new CompositeTransform() { CenterX = 0.5, CenterY = 0.5, Rotation = 0 };
                }
            }

            base.OnOrientationChanged(e);
        }

        private async void Capture_Sequence()
        {

            if (camManual != null)
            {
                if (mode == CaptureMode.DepthMap)
                {
                    try
                    {
                        int w = (int)camManual.PreviewResolution.Width;
                        int h = (int)camManual.PreviewResolution.Height;
                        if (wb == null)
                        {
                            wb = new WriteableBitmap(w, h);
                            MainImage.Width = w;
                            MainImage.Height = h;
                            MainImage.Source = wb;
                        }

                        ShutterButton.IsEnabled = false;
                        CameraCapturePropertyRange range = PhotoCaptureDevice.GetSupportedPropertyRange(CameraSensorLocation.Back, KnownCameraGeneralProperties.ManualFocusPosition);
                        UInt32 max = (UInt32)range.Max;
                        UInt32 min = (UInt32)range.Min;
                        int selMin = 500;
                        Dictionary<int, byte[]> buffers = new Dictionary<int, byte[]>();
                        for (int i = selMin; i < (int)max; i += (int)(50 * (double) selMin / (i * 1.5)))
                        {
                            camManual.SetProperty(KnownCameraGeneralProperties.ManualFocusPosition, i);
                            focusRange = i;
                            buffers.Add(focusRange, new byte[(int)(camManual.PreviewResolution.Width * camManual.PreviewResolution.Height)]);
                            await camManual.FocusAsync();
                            camManual.GetPreviewBufferY(buffers[focusRange]);
                        }
                        //camManual.Dispose();
                        KeyValuePair<short, uint[]>[] sobels = await Task.WhenAll<KeyValuePair<short, uint[]>>(
                                buffers.Select(
                                    pair => Task.Run(
                                        () => new KeyValuePair<short, uint[]>((short)pair.Key, variance(pair.Value, w, h, 8, 6)))));// sobel(pair.Value, w, h) )  )));
                        //destroy buffers
                        buffers.Clear();
                        buffers = null;
                        int len = w * h;
                        int[] maxValues = new int[len];
                        short[] maxFocuses = new short[len];
                        for (int i = 0; i < len; ++i)
                        {
                            maxValues[i] = (int)(sobels[0].Value[i] - sobels[1].Value[i]);
                            maxFocuses[i] = 0;
                        }
                        //Find maximum sobel values
                        for (int i = 1; i < sobels.Length; ++i)
                            for (int j = 1; j < len - 1; j++)
                            {
                                int temp = (int)(sobels[i].Value[j] - (sobels[i].Value[j - 1] + sobels[i].Value[j + 1]) / 2);
                                if (temp > maxValues[j])
                                {
                                    maxValues[j] = temp;
                                    maxFocuses[j] = sobels[i].Key;
                                }
                            }
                        //clear up some memory
                        //maxValues = null;
                        sobels = null;
                        int[] dBuffer = new int[len];
                        //Draw Depth Map
                        double scale = 510.0 / (max - selMin);
                        int focusColor;
                        byte[] d;
                        for (int i = 0; i < len; ++i)
                        {
                            if (maxValues[i] < 5 || maxFocuses[i] < selMin)
                            {
                                d = new byte[] { 0, 0, 0, 255 };
                            }
                            else
                            {
                                focusColor = (int)((double)(maxFocuses[i] - selMin) * scale);
                               /* if (focusColor < 256)
                                    d = new byte[] { (byte)(focusColor), (byte)(255 - focusColor), 0, 255 };
                                else
                                    d = new byte[] { 0, (byte)(255 - (focusColor / 2)), (byte)(focusColor / 2), 255 };
                                */
                                if (focusColor < 256)
                                    d = new byte[] { (byte)(focusColor), 0, 0, 255 };
                                else
                                    d = new byte[] { 255, 0, (byte)(focusColor / 2), 255 };

                            }
                            dBuffer[i] = BitConverter.ToInt32(d, 0);
                        }
                        Deployment.Current.Dispatcher.BeginInvoke(delegate()
                        {
                            // Copy to WriteableBitmap.
                            switch(FilterMode)
                            {
                                case FilterMethod.None:
                                    dBuffer.CopyTo(wb.Pixels, 0);
                                    break;
                                case FilterMethod.Median:
                                    dBuffer.MedianFilter(w, h, FilterSize).CopyTo(wb.Pixels, 0);
                                    break;
                                case FilterMethod.Salt:
                                    dBuffer.SaltFilter(w, h, FilterSize).CopyTo(wb.Pixels, 0);
                                    break;
                                case FilterMethod.Interpolation:
                                    dBuffer.CopyTo(wb.Pixels, 0);
                                    break;
                            }
                            wb.Invalidate();
                            dBuffer = null;
                            ShutterButton.IsEnabled = true;
                            ClearButton.Visibility = System.Windows.Visibility.Visible;
                        });

                        //InitializeCamera(CameraSensorLocation.Back);
                        /*
                        generateSharpnessMap(byteBuffer);

                        //CAPTURE SECOND FOCUS
                        camManual.SetProperty(KnownCameraGeneralProperties.ManualFocusPosition, focusRange + focusStep);
                        await camManual.FocusAsync();
                        camManual.GetPreviewBufferY(byteBuffer);
                        for (int i = 0; i < byteBuffer.Length - 1; i++)
                        {
                            b[0] = byteBuffer[i];
                            b[1] = byteBuffer[i];
                            b[2] = byteBuffer[i];
                            intBuffer[i] = BitConverter.ToInt32(b, 0);

                        }
                        // Save thumbnail as JPEG to the local folder.
                        Deployment.Current.Dispatcher.BeginInvoke(delegate()
                        {
                            // Copy to WriteableBitmap.
                            intBuffer.CopyTo(wb2.Pixels, 0);
                            wb2.Invalidate();

                        });
                        generateSharpnessMap(byteBuffer);

                        
                    /*}
                    else
                    {
                        
                    }*/
                    }
                    catch (Exception eg)
                    {

                        throw;
                    }
                }
                else
                {

                    CameraCapturePropertyRange range = PhotoCaptureDevice.GetSupportedPropertyRange(CameraSensorLocation.Back, KnownCameraGeneralProperties.ManualFocusPosition);
                    UInt32 max = (UInt32)range.Max;
                    UInt32 min = (UInt32)range.Min;

                    List<WriteableBitmap> buffers = new List<WriteableBitmap>();
                    int k = 0;
                    for (int i = 500; i < (int)max; i += 20)
                    {
                        camManual.SetProperty(KnownCameraGeneralProperties.ManualFocusPosition, i);
                        focusRange = i;
                        buffers.Add(new WriteableBitmap((int)camManual.PreviewResolution.Width, (int)camManual.PreviewResolution.Height));
                        await camManual.FocusAsync();
                        camManual.GetPreviewBufferArgb(buffers[k].Pixels);
                        k++;
                    }
                    k = 0;
                    for (int i = 500; i <= max; i += 20)
                    {
                        using (var stream = new MemoryStream())
                        {
                            
                            buffers[k].SaveJpeg(stream, buffers[k].PixelWidth, buffers[k].PixelHeight, 0, 100);
                            stream.Seek(0, SeekOrigin.Begin);
                            new MediaLibrary().SavePicture("focus_at_" + i , stream);
                            k++;
                        }
                    }
                    //byte[] yBuffer = new byte[(int)camManual.PreviewResolution.Width * (int)camManual.PreviewResolution.Height];
                    //for (int i = 0; i < yBuffer.Length; i++)
                        //yBuffer[i] = (byte) (i/((int)camManual.PreviewResolution.Width));
                         
                    //int[] sharpnessTest = sobel(ref yBuffer, (int)camManual.PreviewResolution.Width, (int)camManual.PreviewResolution.Height);
                    
                }
            }
        }

        private async void ShutterButton_Click(object sender, RoutedEventArgs e)
        {
            Capture_Sequence();
        }

        // Informs when full resolution photo has been taken, saves to local media library and the local folder.
        void cam_CaptureImageAvailable(object sender, Microsoft.Devices.ContentReadyEventArgs e)
        {
            string fileName = savedCounter + ".jpg";

            try
            {   // Write message to the UI thread.
                Deployment.Current.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "Captured image available, saving photo.";
                });

                // Save photo to the media library camera roll.
                library.SavePictureToCameraRoll(fileName, e.ImageStream);

                // Write message to the UI thread.
                Deployment.Current.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "Photo has been saved to camera roll.";

                });

                // Set the position of the stream back to start
                e.ImageStream.Seek(0, SeekOrigin.Begin);

                // Save photo as JPEG to the local folder.
                using (IsolatedStorageFile isStore = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    using (IsolatedStorageFileStream targetStream = isStore.OpenFile(fileName, FileMode.Create, FileAccess.Write))
                    {
                        // Initialize the buffer for 4KB disk pages.
                        byte[] readBuffer = new byte[4096];
                        int bytesRead = -1;

                        // Copy the image to the local folder. 
                        while ((bytesRead = e.ImageStream.Read(readBuffer, 0, readBuffer.Length)) > 0)
                        {
                            targetStream.Write(readBuffer, 0, bytesRead);
                        }
                    }
                }

                // Write message to the UI thread.
                Deployment.Current.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "Photo has been saved to the local folder.";

                });
            }
            finally
            {
                // Close image stream
                e.ImageStream.Close();
            }

        }

        // Informs when thumbnail photo has been taken, saves to the local folder
        // User will select this image in the Photos Hub to bring up the full-resolution. 
        public void cam_CaptureThumbnailAvailable(object sender, ContentReadyEventArgs e)
        {
            string fileName = savedCounter + "_th.jpg";

            try
            {
                // Write message to UI thread.
                Deployment.Current.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "Captured image available, saving thumbnail.";
                });

                // Save thumbnail as JPEG to the local folder.
                using (IsolatedStorageFile isStore = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    using (IsolatedStorageFileStream targetStream = isStore.OpenFile(fileName, FileMode.Create, FileAccess.Write))
                    {
                        // Initialize the buffer for 4KB disk pages.
                        byte[] readBuffer = new byte[4096];
                        int bytesRead = -1;

                        // Copy the thumbnail to the local folder. 
                        while ((bytesRead = e.ImageStream.Read(readBuffer, 0, readBuffer.Length)) > 0)
                        {
                            targetStream.Write(readBuffer, 0, bytesRead);
                        }
                    }
                }

                // Write message to UI thread.
                Deployment.Current.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "Thumbnail has been saved to the local folder.";

                });
            }
            finally
            {
                // Close image stream
                e.ImageStream.Close();
            }
        }

        /*private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            focusRange = (int) e.NewValue;
            if (s == CameraFocusStatus.Locked)
            {
                changeFocus();
                textBox.Text = "" + focusRange;
            }
        }*/

        private async void changeFocus() {
            camManual.SetProperty(KnownCameraGeneralProperties.ManualFocusPosition, focusRange);
            s = await camManual.FocusAsync();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Deployment.Current.Dispatcher.BeginInvoke(delegate()
            {
                int[] b = new int[wb.Pixels.Length];
                for  (int i = 0; i < wb.Pixels.Length; i++)
                    b[i] = 0;
                b.CopyTo(wb.Pixels, 0);
                b.CopyTo(depthBuffer, 0);
                wb.Invalidate();
            });
            ClearButton.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void generateSharpnessMap(byte[] data, int focusValue)
        {
            // Dimensions of the original frame
            int w = (int)camManual.PreviewResolution.Width;
            int h = (int)camManual.PreviewResolution.Height;

            // and the scaled down sub-image
            int ws = w / _sharpnessArea[0];
            int hs = h / _sharpnessArea[1];

            CameraCapturePropertyRange range = PhotoCaptureDevice.GetSupportedPropertyRange(CameraSensorLocation.Back, KnownCameraGeneralProperties.ManualFocusPosition);
                    UInt32 max = (UInt32)range.Max;
                    UInt32 min = (UInt32)range.Min;
            // Generate a new buffer for the sharpness map. CAUTION: in production code we should check the dimension as well
            // because the preview resolution might have changed.
            ///if (_sharpnessMap == null)
            //{
            //    _sharpnessMap = new int[hs * ws];
            //}
            if (bufferMapNew == null)
            {
                bufferMapNew = new FocusMap(ws, hs);
                bufferMapNew.threshold = threshold;
            }

            
            // Calculate sharpness for each sub image
            /*
            int x, y;
            int sharpness;
            for (y = 0; y < hs; y++)
            {
                for (x = 0; x < ws; x++)
                {
                    sharpness = calculateSharpness_variance(x * ws, y * hs, ws, hs, w, data);
                    bufferMapNew.set(y * _sharpnessArea[0] + x, sharpness);
                }
            }
             */
            if (true)//(bool)checkboxSave.IsChecked)
                bufferMapNew.Map = sobel(data, w, h);
            else
                bufferMapNew.Map = calculateSharpness_variance(data, w, h, 3);
            if (bufferMapOld == null)
            {
                bufferMapOld = new FocusMap(ws, hs);
                bufferMapOld.Map = bufferMapNew.Map;
            }
            bool[] cmpSet = bufferMapNew > bufferMapOld;
            int focusColor = (int) ((double)(focusValue - 500) / (max - 500) * 510);
            int[] testIndices = new int[depthBuffer.Length];
            int prevLoc = 0;
            byte [] d;
            for (int k = 0; k < cmpSet.Length; k++)
            {
                if (cmpSet[k] && bufferMapNew.Triggers[k] != FocusMap.Trigger.Drawn)
                    bufferMapNew.Triggers[k] = FocusMap.Trigger.Triggered;
                else if (bufferMapOld.Triggers[k] == FocusMap.Trigger.Triggered)
                {
                    bufferMapNew.Triggers[k] = FocusMap.Trigger.Drawn;
                    int location = (int)((double)(k / ws) / hs * h * w + ((double)(k % ws) / ws * w));
                    //if firstDraw == null set it here. Reset on SH button click.
                    for (int y = 0; y < _sharpnessArea[1]; y++)
                        for (int x = 0; x < _sharpnessArea[0]; x++)
                        {
                            //on 'firstDraw' scale focusColor to begin at 
                            //the corresponding focusRange instead of just at 500

                            //use if statement to enable tri-color setup
                            //if x < 128 go blue to green, else green to red
                            if (focusColor < 256)
                                d = new byte[] { (byte)(focusColor), (byte)(255 - focusColor), 0, 255 };
                            else
                                d = new byte[] { 0, (byte)(255 - (focusColor/2)), (byte)(focusColor/2), 255 };
                            depthBuffer[location + y * w + x] = BitConverter.ToInt32(d, 0);
                            //begin at (bottom) outer edges and fill in according to color and 
                            //direction of edge concavity. paint onto separate buffer so 
                            //that inner items can be painted possibly on top of outer items 
                            //(if outer items were painted too large this will make up for it)
                        }
                    if (location != prevLoc)
                    {
                        //prevLoc = location;

                    }
                }
            }
            bufferMapOld.Triggers = bufferMapNew.Triggers;
            bufferMapOld.Map = bufferMapNew.Map;
        }

        private uint[] calculateSharpness_variance(byte[] inArr, int width, int height, int stride)
        {
            double lumSum = 0;
            double lumSquared = 0;
            double lum = 0;
            if (stride % 2 == 0)
                stride++;
            if (stride < 3)
                stride = 3;
            int numPixels = stride * stride;
            uint[] outArr = new uint[inArr.Length];
            int halfStride = (stride / 2);
            int bufWidth = (width + 2 * halfStride);
            byte[] buffer = new byte[bufWidth * stride];

            //Initialize buffer
            int i;
            for (i = 0; i < halfStride + 1; i++)
            {
                for (int j = 0; j < halfStride; j++ )
                {
                    buffer[i * bufWidth + j] = inArr[0];
                    buffer[i * bufWidth + width + 1 + j] = inArr[width - 1];
                }
                
                for (int j = 0; j < width; j++)
                {
                    buffer[i * bufWidth + j + 1] = inArr[j];
                }

            }
            for (i = halfStride + 1; i < stride - 1; i++)
            {
                for (int j = 0; j < halfStride; j++)
                {
                    buffer[i * bufWidth + j] = inArr[0];
                    buffer[i * bufWidth + width + 1 + j] = inArr[width - 1];
                }
                for (int j = 0; j < width; j++)
                {
                    buffer[i * bufWidth + j + 1] = inArr[(i - halfStride) * width + j];
                }

            }
            int[] buf = Enumerable.Range(0, stride).ToArray<int>();
            int y = 0;

            //Calculate Luminance values
            for (y = 0; y < height - halfStride; y++)
            {
                //write newest buffer row
                //sides
                for (int j = 0; j < halfStride; j++)
                {
                    buffer[buf[stride - 1] * bufWidth + j] = inArr[(y + halfStride) * width];
                    buffer[buf[stride - 1] * bufWidth + width + 1 + j] = inArr[(y + halfStride) * width + width - 1];
                }
                //inner
                for (int j = 0; i < width; i++)
                    buffer[buf[stride - 1] * bufWidth + j + 1] = inArr[(y + halfStride) * width + j];

                //calc luminance
                for (int x = halfStride; x < width + halfStride; x++)
                {
                    for (int k = 0; k < stride; k++)
                        for (int j = -halfStride; j <= halfStride; j++ )
                            lum += buffer[(buf[k] * bufWidth) + x + j];

                    lumSum = (lum / numPixels);
                    lumSquared = ((lum * lum) / numPixels);
                    outArr[y * width + x - halfStride] = (uint)(lumSquared - (lumSum * lumSum));
                    lum = 0;
                }
                //shuffle buffer. oldest data gets overwritten
                for (int k = 0; k < stride; k++)
                    buf[k] = (buf[k] + 1) % stride;
            }

            //last rows
            for (; y < height + halfStride; y++)
            {
                for (int x = halfStride; x < width + halfStride; x++)
                {
                    for (int k = 0; k < stride; k++)
                        for (int j = -halfStride; j <= halfStride; j++)
                            lum += buffer[(buf[k] * bufWidth) + x + j];

                    lumSum = (lum / numPixels);
                    lumSquared = ((lum * lum) / numPixels);
                    outArr[(y - halfStride) * width + x - halfStride] = (uint)(lumSquared - (lumSum * lumSum));
                    lum = 0;
                }
                if (y < height + halfStride - 1)
                {
                    for (int k = 0; k < stride; k++)
                        buf[k] = (buf[k] + 1) % stride;
                    for (int j = 0; j < halfStride; j++)
                    {
                        buffer[buf[stride - 1] * bufWidth + j] = inArr[(height - 1) * width];
                        buffer[buf[stride - 1] * bufWidth + width + 1 + j] = inArr[(height - 1) * width + width - 1];
                    }
                    for (int j = 0; i < width; i++)
                        buffer[buf[stride - 1] * (bufWidth) + j + 1] = inArr[(height - 1) * width + j];
                }
            }

            return outArr;

            for (y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    lum = inArr[y * width + x];

                    lumSum= (lum / numPixels);
                    lumSquared = ((lum * lum) / numPixels);
                    outArr[y * width + x] = (uint)(lumSquared - (lumSum * lumSum));
                }
                     //+= (int)((sum * sum + vertical * vertical + 1170450.0) / 2340900.0 * 255);

            return outArr;
        }

        /*private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                int val =  int.Parse(textBox.Text);
                if (val > 100)
                {
                    val = 100;
                    textBox.Text = "100";
                }
                else if (val < 0)
                {
                    val = 0;
                    textBox.Text = "0";
                }
                if (bufferMapNew == null)
                    threshold = val;
                else
                    bufferMapNew.threshold = val;
            }
            catch (Exception ex)
            {
                throw;
            }

        }

        private void textBox_GotFocus(object sender, RoutedEventArgs e)
        {
            textBox.SelectAll();
        }
        */

        public uint[] median(uint[] inArr, int w, int h, int kernel)
        {
            uint[] outArr = new uint[inArr.Length];
            Dictionary<int, uint[]> buffer = new Dictionary<int, uint[]>(kernel);
            int before = kernel/2;
            int inside = kernel - before;
            int i;
            /**COPY INTO BUFFERS**/
            for (i = 0; i < before; i++)
            {
                buffer.Add(i, new uint[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[0];
                    buffer[i][j + before + w] = inArr[w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            for (i = before; i < kernel; i++)
            {
                buffer.Add(i, new uint[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[i*w];
                    buffer[i][j + before + w] = inArr[i * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            /**FINISH BUFFER COPY**/

            /**PERFORM OPERATION**/
            uint[] sortList = new uint[kernel * kernel];
            int b = 0;
            int p, n;
            for (i = 0; i < h; i++)
            {
                for (n = 0; n < w; n++)
                {
                    for (p = 0; p < kernel; ++p)
                        buffer[p].SubArray(n, kernel).CopyTo(sortList, p * kernel);
                    outArr[i * w + n] = sortList.Median();
                }

                //new buffer line
                int iMod = Math.Min(i + kernel, h - 1); 
                for (int j = 0; j < before; j++)
                {
                    buffer[b][j] = inArr[iMod * w];
                    buffer[b][j + before + w] = inArr[iMod * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[b][k] = inArr[iMod * w + k];
                b = (b + 1) % kernel;
            }
            /**FINISH OPERATION**/

            return outArr;
        }
        private uint[] variance(byte[] inArr, int w, int h, int x, int y)
        {
            uint[] outArr = new uint[inArr.Length];
            int section_count = x * y;
            uint[] outMidArr = new uint[section_count];
            byte[] inMidArr = new byte[section_count];
            int steps_x = w / x;
            int steps_y = h / y;
            for (int i = 0; i < steps_y; i++)
                for (int j = 0; j < steps_x; j++)
                {
                    for (int n = 0; n < section_count; n++)
                        inMidArr[n] = inArr[((y * i) + (n / x)) * w + (x * j) + (n % x)];
                    variance_sections(inMidArr, x, y);
                    for (int n = 0; n < section_count; n++)
                        outArr[((y * i) + (n / x)) * w + (x * j) + (n % x)] = inMidArr[n];
                }
            return outArr;
        }

        private uint[] variance_sections(byte[] inArr, int w, int h)
        {
            int count = inArr.Length;
            uint[] outArr = new uint[count];
            int lum = 0;
            int lumSum = 0;
            int lumSquared = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    lum = inArr[y * w + x];

                    lumSum = (lum / count);
                    lumSquared = ((lum * lum) / count);
                    outArr[y * w + x] = (uint)(lumSquared - (lumSum * lumSum));
                }
                     //+= (int)((sum * sum + vertical * vertical + 1170450.0) / 2340900.0 * 255);

            return outArr;

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inArr"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private uint[] sobel(byte[] inArr, int width, int height)
        {
            int sWidth = _sharpnessArea[0];
            int sHeight = _sharpnessArea[1];
            byte[] buffer = new byte[(width + 2) * 3];
            uint[] outArr = new uint[inArr.Length];
            for (int i = 0; i < outArr.Length; ++i)
                outArr[i] = 0;
            for (int i = 0; i < 2; i++)
            {
                buffer[i * (width + 2)] = inArr[0];
                buffer[i * (width + 2) + width + 1] = inArr[width - 1];
                for (int j = 0; j < width; j++)
                {
                    buffer[i * (width + 2) + j + 1] = inArr[j];
                }

            }

            int buf1 = 0;
            int buf2 = 1;
            int buf3 = 2;

            /*test variables */
            int[] test = new int[12];
            int horizontal;
            int vertical;
            int y = 0;
            double scale = 255.0/1170450.0;
            for (y = 0; y < height - 2; y++)
            {
                buffer[buf3 * (width + 2)] = inArr[(y + 1) * width];
                buffer[buf3 * (width + 2) + width + 1] = inArr[(y + 1) * width + width - 1];
                for (int i = 0; i < width; i++)
                    buffer[buf3 * (width + 2) + i + 1] = inArr[(y + 1) * width + i];
                for (int x = 1; x < width + 1; x++)
                {
                    horizontal = buffer[(buf1 * (width + 2)) + x - 1] + 2 * buffer[(buf1 * (width + 2)) + x] + buffer[(buf1 * (width + 2)) + x + 1]
                        - buffer[(buf3 * (width + 2)) + x - 1] - 2 * buffer[(buf3 * (width + 2)) + x] - buffer[(buf3 * (width + 2)) + x + 1];
                    vertical = -buffer[(buf1 * (width + 2)) + x - 1] - 2*buffer[(buf2 * (width + 2)) + x - 1] - buffer[(buf3 * (width + 2)) + x - 1]
                        + buffer[(buf1 * (width + 2)) + x + 1] + 2*buffer[(buf2 * (width + 2)) + x + 1] + buffer[(buf3 * (width + 2)) + x + 1];

                    outArr[y * width + x - 1] += (uint)((horizontal * horizontal + vertical * vertical));
                }
                buf1 = (y) % 3;
                buf2 = (y + 1) % 3;
                buf3 = (y + 2) % 3;

            }
            
            for (; y < height + 1; y++)
            {
                for (int x = 1; x < width + 1; x++)
                {
                    horizontal = buffer[(buf1 * (width + 2)) + x - 1] + 2 * buffer[(buf1 * (width + 2)) + x] + buffer[(buf1 * (width + 2)) + x + 1]
                        - buffer[(buf3 * (width + 2)) + x - 1] - 2 * buffer[(buf3 * (width + 2)) + x] - buffer[(buf3 * (width + 2)) + x + 1];
                    vertical = -buffer[(buf1 * (width + 2)) + x - 1] - 2 * buffer[(buf2 * (width + 2)) + x - 1] - buffer[(buf3 * (width + 2)) + x - 1]
                        + buffer[(buf1 * (width + 2)) + x + 1] + 2 * buffer[(buf2 * (width + 2)) + x + 1] + buffer[(buf3 * (width + 2)) + x + 1];

                    outArr[(y - 1) * width + x - 1] += (uint)((horizontal * horizontal + vertical * vertical));
                }
                if (y < height + 1)
                {
                    buf1 = (y - 1) % 3;
                    buf2 = y % 3;
                    buf3 = (y + 1) % 3;
                    buffer[buf1 * (width + 2)] = inArr[(height - 1) * width];
                    buffer[buf1 * (width + 2) + width + 1] = inArr[(height - 1) * width + width - 1];
                    for (int i = 0; i < width; i++)
                        buffer[((y - 1) % 3) * (width + 2) + i + 1] = inArr[(height - 1) * width + i];
                }
            }

                return outArr;
        }

        void FilterSizeTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            FilterSizeTextBox.SelectAll();
        }

        private void FilterSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FilterSize = int.Parse(FilterSizeTextBox.Text);
            FilterSizeTextBox.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ChooseFilterSize_Click(object sender, EventArgs e)
        {
            FilterSizeTextBox.Visibility = System.Windows.Visibility.Visible;
            FilterSizeTextBox.Focus();
        }

        void FilterChoice_LostFocus(object sender, RoutedEventArgs e)
        {
            AnimateFilterListOut.Begin();
        }

        void FilterChoiceInterpolation_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            if (FilterChoice.ListPickerMode == ListPickerMode.Expanded)
                AnimateFilterListOut.Begin();
            FilterMode = FilterMethod.Interpolation;
        }

        void FilterChoiceNone_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            if (FilterChoice.ListPickerMode == ListPickerMode.Expanded)
                AnimateFilterListOut.Begin();
            FilterMode = FilterMethod.None;
        }

        void FilterChoiceSalt_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            if (FilterChoice.ListPickerMode == ListPickerMode.Expanded)
                AnimateFilterListOut.Begin();
            FilterMode = FilterMethod.Salt;
        }

        void FilterChoiceMedian_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            if (FilterChoice.ListPickerMode == ListPickerMode.Expanded)
                AnimateFilterListOut.Begin();
            FilterMode = FilterMethod.Median;
        }

        void ChooseFilter_Click(object sender, EventArgs e)
        {
            FilterChoice.Open();
            AnimateFilterListIn.Begin();
        }

        void ApplicationBar_StateChanged(object sender, ApplicationBarStateChangedEventArgs e)
        {
            if (e.IsMenuVisible)
                ApplicationBar.BackgroundColor = Color.FromArgb(175, 64, 64, 64);
            else
                ApplicationBar.BackgroundColor = Color.FromArgb(0, 64, 64, 64);
        }

        void appBarMenuItem_Click(object sender, EventArgs e)
        {

            ApplicationBarMenuItem appBarMenuItem = (ApplicationBarMenuItem)sender;
            if (mode == CaptureMode.DepthMap)
            {
                mode = CaptureMode.FocusSweepSave;
                appBarMenuItem.Text = "Switch to Depth Map Mode";
            }
            else
            {
                mode = CaptureMode.DepthMap;
                appBarMenuItem.Text = "Switch to Focus Sweep Save";
            }
        }

        
    }

    public static class Extensions
    {
        public static T[] SubArray<T>(this T[] data, int index, int length)
        {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        public static T Median<T>(this T[] data)
        {
            if (data.Length == 1)
                return data[0];
            Array.Sort(data);

            return data[data.Length / 2];
        }

        public static T Median<T>(this T[] data, T avoidValue)
        {
            if (data.Length == 1)
                return data[0];
            Array.Sort(data);
            int i = data.Length / 2;
            T result = data[i];
            while (i < data.Length && result.Equals(avoidValue))
                result = data[i++];
            return data[data.Length / 2];
        }

        public static T Salt<T>(this T[] data)
        {
            if (data.Length == 1)
                return data[0];
            Array.Sort(data);
            return data[data.Length - 1];
        }

        public static T[] MedianFilter<T>(this T[] inArr, int w, int h, int kernel, T avoidValue)
        {
            if (kernel <= 1)
                return inArr;
            if (kernel % 2 == 0)
                kernel++;
            T[] outArr = new T[inArr.Length];
            Dictionary<int, T[]> buffer = new Dictionary<int, T[]>(kernel);
            int before = kernel / 2;
            int i;
            /**COPY INTO BUFFERS**/
            for (i = 0; i < before; i++)
            {
                buffer.Add(i, new T[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[0];
                    buffer[i][j + before + w] = inArr[w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            for (i = before; i < kernel; i++)
            {
                buffer.Add(i, new T[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[i * w];
                    buffer[i][j + before + w] = inArr[i * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            /**FINISH BUFFER COPY**/

            /**PERFORM OPERATION**/
            T[] sortList = new T[kernel * kernel];
            int b = 0;
            int p, n;
            for (i = 0; i < h; i++)
            {
                for (n = 0; n < w; n++)
                {
                    for (p = 0; p < kernel; ++p)
                        buffer[p].SubArray(n, kernel).CopyTo(sortList, p * kernel);
                    outArr[i * w + n] = sortList.Median(avoidValue);
                }

                //new buffer line
                int iMod = Math.Min(i + kernel, h - 1);
                for (int j = 0; j < before; j++)
                {
                    buffer[b][j] = inArr[iMod * w];
                    buffer[b][j + before + w] = inArr[iMod * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[b][k] = inArr[iMod * w + k];
                b = (b + 1) % kernel;
            }
            /**FINISH OPERATION**/

            return outArr;
        }

        public static T[] MedianFilter<T>(this T[] inArr, int w, int h, int kernel)
        {
            if (kernel <= 1)
                return inArr;
            if (kernel % 2 == 0)
                kernel++;
            T[] outArr = new T[inArr.Length];
            Dictionary<int, T[]> buffer = new Dictionary<int, T[]>(kernel);
            int before = kernel / 2;
            int i;
            /**COPY INTO BUFFERS**/
            for (i = 0; i < before; i++)
            {
                buffer.Add(i, new T[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[0];
                    buffer[i][j + before + w] = inArr[w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            for (i = before; i < kernel; i++)
            {
                buffer.Add(i, new T[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[i * w];
                    buffer[i][j + before + w] = inArr[i * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            /**FINISH BUFFER COPY**/

            /**PERFORM OPERATION**/
            T[] sortList = new T[kernel * kernel];
            int b = 0;
            int p, n;
            for (i = 0; i < h; i++)
            {
                for (n = 0; n < w; n++)
                {
                    for (p = 0; p < kernel; ++p)
                        buffer[p].SubArray(n, kernel).CopyTo(sortList, p * kernel);
                    outArr[i * w + n] = sortList.Median();
                }

                //new buffer line
                int iMod = Math.Min(i + kernel, h - 1);
                for (int j = 0; j < before; j++)
                {
                    buffer[b][j] = inArr[iMod * w];
                    buffer[b][j + before + w] = inArr[iMod * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[b][k] = inArr[iMod * w + k];
                b = (b + 1) % kernel;
            }
            /**FINISH OPERATION**/

            return outArr;
        }

        public static T[] SaltFilter<T>(this T[] inArr, int w, int h, int kernel)
        {
            if (kernel <= 1)
                return inArr;
            if (kernel % 2 == 0)
                kernel++;
            T[] outArr = new T[inArr.Length];
            Dictionary<int, T[]> buffer = new Dictionary<int, T[]>(kernel);
            int before = kernel / 2;
            int i;
            /**COPY INTO BUFFERS**/
            for (i = 0; i < before; i++)
            {
                buffer.Add(i, new T[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[0];
                    buffer[i][j + before + w] = inArr[w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            for (i = before; i < kernel; i++)
            {
                buffer.Add(i, new T[w + kernel - 1]);
                for (int j = 0; j < before; j++)
                {
                    buffer[i][j] = inArr[i * w];
                    buffer[i][j + before + w] = inArr[i * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[i][k] = inArr[i * w + k];
            }
            /**FINISH BUFFER COPY**/

            /**PERFORM OPERATION**/
            T[] sortList = new T[kernel * kernel];
            int b = 0;
            int p, n;
            for (i = 0; i < h; i++)
            {
                for (n = 0; n < w; n++)
                {
                    for (p = 0; p < kernel; ++p)
                        buffer[p].SubArray(n, kernel).CopyTo(sortList, p * kernel);
                    outArr[i * w + n] = sortList.Salt();
                }

                //new buffer line
                int iMod = Math.Min(i + kernel, h - 1);
                for (int j = 0; j < before; j++)
                {
                    buffer[b][j] = inArr[iMod * w];
                    buffer[b][j + before + w] = inArr[iMod * w + w - 1];
                }
                for (int k = 0; k < w; k++)
                    buffer[b][k] = inArr[iMod * w + k];
                b = (b + 1) % kernel;
            }
            /**FINISH OPERATION**/

            return outArr;
        }
    }
}