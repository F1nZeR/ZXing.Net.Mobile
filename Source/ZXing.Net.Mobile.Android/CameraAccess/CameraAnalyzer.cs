using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ApxLabs.FastAndroidCamera;

namespace ZXing.Mobile.CameraAccess
{
    public class CameraAnalyzer
    {
        private readonly CameraController _cameraController;
        private DateTime _lastPreviewAnalysis = DateTime.UtcNow;
        private bool _wasScanned;
        private readonly IScannerSessionHost _scannerHost;

        public CameraAnalyzer(CameraController cameraController, IScannerSessionHost scannerHost)
        {
            _scannerHost = scannerHost;
            _cameraController = cameraController;

            Torch = new Torch(_cameraController);
        }

        public event EventHandler<Result> BarcodeFound;

        public Torch Torch { get; }

        public bool IsAnalyzing { get; private set; }

        public void PauseAnalysis()
        {
            IsAnalyzing = false;
        }

        public void ResumeAnalysis()
        {
            IsAnalyzing = true;
        }

        public void ShutdownCamera()
        {
            IsAnalyzing = false;
            _cameraController.OnPreviewFrameReady -= HandleOnPreviewFrameReady;
            _cameraController.ShutdownCamera();
        }

        public void SetupCamera()
        {
            _cameraController.OnPreviewFrameReady += HandleOnPreviewFrameReady;
            _cameraController.SetupCamera();
        }

        public void AutoFocus()
        {
            _cameraController.AutoFocus();
        }

        public void AutoFocus(int x, int y)
        {
            _cameraController.AutoFocus(x, y);
        }

        public void RefreshCamera()
        {
            _cameraController.RefreshCamera();
        }

        private bool CanAnalyzeFrame
        {
            get
            {
				if (!IsAnalyzing)
					return false;
				
                var elapsedTimeMs = (DateTime.UtcNow - _lastPreviewAnalysis).TotalMilliseconds;
				if (elapsedTimeMs < _scannerHost.ScanningOptions.DelayBetweenAnalyzingFrames)
					return false;
				
				// Delay a minimum between scans
				if (_wasScanned && elapsedTimeMs < _scannerHost.ScanningOptions.DelayBetweenContinuousScans)
					return false;
				
				return true;
            }
        }

        private void HandleOnPreviewFrameReady(object sender, FastJavaByteArray fastArray)
        {
            if (!CanAnalyzeFrame)
                return;

            _wasScanned = false;
            _lastPreviewAnalysis = DateTime.UtcNow;

            try 
            {
                DecodeFrame(fastArray);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Debug(MobileBarcodeScanner.TAG, $"DecodeFrame exception occurred: {ex.Message}");
            }
        }

		private byte[] buffer;
        private void DecodeFrame(FastJavaByteArray fastArray)
        {
            var cameraParameters = _cameraController.Camera.GetParameters();
            var width = cameraParameters.PreviewSize.Width;
            var height = cameraParameters.PreviewSize.Height;

            var barcodeReader = _scannerHost.ScanningOptions.BuildBarcodeReader();

            // use last value for performance gain
            var cDegrees = _cameraController.LastCameraDisplayOrientationDegree;
			var rotate = (cDegrees == 90 || cDegrees == 270);

            Result result = null;
            var start = PerformanceCounter.Start();

            if (rotate) 
            {
                fastArray.Transpose(ref buffer, width, height);
                var tmp = width;
                width = height;
                height = tmp;
            }
			
            var luminanceSource = new FastJavaByteArrayYUVLuminanceSource(fastArray, width, height, 0, 0, width, height); // _area.Left, _area.Top, _area.Width, _area.Height);
            
            result = barcodeReader.Decode(luminanceSource);

            PerformanceCounter.Stop(start, "Decode Time: {0} ms (width: " + width + ", height: " + height + ", degrees: " + cDegrees + ", rotate: " + rotate + ")");

            if (result != null)
            {
                Android.Util.Log.Debug(MobileBarcodeScanner.TAG, "Barcode Found: " + result.Text);

                _wasScanned = true;
                BarcodeFound?.Invoke(this, result);
            }
            else
            {
                AutoFocus();
            }
        }
    }
}