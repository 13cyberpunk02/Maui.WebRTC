using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Java.Lang;

namespace Maui.WebRTC;

public sealed partial class CameraCapturer
{
    Session? _session;

    public static partial int DeviceCount => Manager.GetCameraIdList().Length;

    static CameraManager Manager =>
        (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService)!;

    partial void StartPlatform(VideoSource source, int width, int height, int fps, CameraFacing facing)
    {
        var cameraId = PickCamera(facing == CameraFacing.Back ? LensFacing.Back : LensFacing.Front, out var found);
        var session = new Session(source, cameraId, found, width, height, fps);
        session.Open();
        _session = session;
        Facing = found == LensFacing.Front ? CameraFacing.Front : CameraFacing.Back;
    }

    partial void SwitchPlatform(VideoSource source, int width, int height, int fps)
    {
        var next = Facing == CameraFacing.Front ? CameraFacing.Back : CameraFacing.Front;
        StopPlatform();
        StartPlatform(source, width, height, fps, next);
    }

    partial void StopPlatform()
    {
        var session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();
    }

    /// <summary>The first camera facing the wanted way, or else the first camera there is.</summary>
    static string PickCamera(LensFacing wanted, out LensFacing facing)
    {
        var ids = Manager.GetCameraIdList();
        if (ids.Length == 0)
            throw new WebRtcException("This device has no camera.");

        foreach (var id in ids)
        {
            if (FacingOf(id) == wanted)
            {
                facing = wanted;
                return id;
            }
        }

        facing = FacingOf(ids[0]) ?? LensFacing.Back;
        return ids[0];
    }

    static LensFacing? FacingOf(string id)
    {
        var value = (Integer?)Manager.GetCameraCharacteristics(id).Get(CameraCharacteristics.LensFacing!);
        return value is null ? null : (LensFacing)value.IntValue();
    }

    sealed class Session(VideoSource source, string cameraId, LensFacing facing, int width, int height, int fps) : IDisposable
    {
        readonly HandlerThread _thread = new("Maui.WebRTC.Camera");
        Handler? _handler;
        ImageReader? _reader;
        CameraDevice? _device;
        CameraCaptureSession? _capture;
        int _sensorOrientation;

        public void Open()
        {
            _thread.Start();
            _handler = new Handler(_thread.Looper!);

            var characteristics = Manager.GetCameraCharacteristics(cameraId);
            _sensorOrientation = ((Integer)characteristics.Get(CameraCharacteristics.SensorOrientation!)!).IntValue();

            var map = (global::Android.Hardware.Camera2.Params.StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap!)!;
            var size = map.GetOutputSizes((int)ImageFormatType.Yuv420888)!
                .OrderBy(s => System.Math.Abs(s.Width * s.Height - width * height))
                .First();

            _reader = ImageReader.NewInstance(size.Width, size.Height, ImageFormatType.Yuv420888, 2);
            _reader.SetOnImageAvailableListener(new ImageListener(this), _handler);
            Manager.OpenCamera(cameraId, new DeviceCallback(this), _handler);
        }

        void OnOpened(CameraDevice device)
        {
            _device = device;
            var surface = _reader!.Surface!;
#pragma warning disable CA1422 // The replacement takes a SessionConfiguration but the outcome is the same on every API level we support.
            device.CreateCaptureSession([surface], new SessionCallback(this, surface), _handler);
#pragma warning restore CA1422
        }

        void OnConfigured(CameraCaptureSession capture, Surface surface)
        {
            _capture = capture;
            var request = _device!.CreateCaptureRequest(CameraTemplate.Record);
            request.AddTarget(surface);
            request.Set(CaptureRequest.ControlAeTargetFpsRange!, new global::Android.Util.Range(Integer.ValueOf(System.Math.Min(15, fps)), Integer.ValueOf(fps)));
            capture.SetRepeatingRequest(request.Build(), null, _handler);
        }

        unsafe void OnImageAvailable(ImageReader reader)
        {
            using var image = reader.AcquireLatestImage();
            if (image is null)
                return;

            // Dispose only releases the C# wrappers; Close is what hands the image back to the reader.
            // Everything per-frame is disposed too, or the JNI references run out within minutes.
            var planes = image.GetPlanes()!;
            try
            {
                var y = planes[0];
                var u = planes[1];
                var v = planes[2];
                using var yBuffer = y.Buffer!;
                using var uBuffer = u.Buffer!;
                using var vBuffer = v.Buffer!;
                source.PushRaw(
                    image.Width,
                    image.Height,
                    (byte*)JNIEnv.GetDirectBufferAddress(yBuffer.Handle),
                    y.RowStride,
                    (byte*)JNIEnv.GetDirectBufferAddress(uBuffer.Handle),
                    u.RowStride,
                    (byte*)JNIEnv.GetDirectBufferAddress(vBuffer.Handle),
                    v.RowStride,
                    u.PixelStride,
                    CurrentRotation());
            }
            finally
            {
                foreach (var plane in planes)
                    plane.Dispose();
                image.Close();
            }
        }

        int CurrentRotation()
        {
            using var service = global::Android.App.Application.Context.GetSystemService(Context.WindowService)!;
            using var windowManager = service.JavaCast<IWindowManager>()!;
#pragma warning disable CA1422 // Context.Display needs an activity, which a capturer doesn't have.
            using var display = windowManager.DefaultDisplay!;
            var device = display.Rotation switch
            {
                SurfaceOrientation.Rotation90 => 90,
                SurfaceOrientation.Rotation180 => 180,
                SurfaceOrientation.Rotation270 => 270,
                _ => 0,
            };
#pragma warning restore CA1422
            return facing == LensFacing.Front
                ? (_sensorOrientation + device) % 360
                : (_sensorOrientation - device + 360) % 360;
        }

        public void Dispose()
        {
            _capture?.Close();
            _device?.Close();
            _reader?.Close();
            _thread.QuitSafely();
            _thread.Join();
        }

        sealed class ImageListener(Session session) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
        {
            public void OnImageAvailable(ImageReader? reader) => session.OnImageAvailable(reader!);
        }

        sealed class DeviceCallback(Session session) : CameraDevice.StateCallback
        {
            public override void OnOpened(CameraDevice camera) => session.OnOpened(camera);
            public override void OnDisconnected(CameraDevice camera) => camera.Close();
            public override void OnError(CameraDevice camera, [GeneratedEnum] CameraError error) => camera.Close();
        }

        sealed class SessionCallback(Session owner, Surface surface) : CameraCaptureSession.StateCallback
        {
            public override void OnConfigured(CameraCaptureSession session) => owner.OnConfigured(session, surface);
            public override void OnConfigureFailed(CameraCaptureSession session) { }
        }
    }
}
