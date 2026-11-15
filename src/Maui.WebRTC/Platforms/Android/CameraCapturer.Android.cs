using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Java.Lang;
using NativeCamera = Android.Hardware.Camera2.CameraDevice;

namespace Maui.WebRTC;

public sealed partial class CameraCapturer
{
    Session? _session;

    public static partial int DeviceCount => Devices.Count;

    /// <summary>
    /// The main camera of each side, plus a USB camera if one is plugged in. Phones list more back cameras
    /// (wide, macro, …), but those are the main camera's business, not a separate choice in a call.
    /// </summary>
    public static partial IReadOnlyList<CameraDevice> Devices
    {
        get
        {
            var ids = Manager.GetCameraIdList();
            var devices = new List<CameraDevice>();
            foreach (var (wanted, facing, name) in new[]
            {
                (LensFacing.Front, CameraFacing.Front, "Front camera"),
                (LensFacing.Back, CameraFacing.Back, "Back camera"),
                (LensFacing.External, CameraFacing.External, "USB camera"),
            })
            {
                var index = Array.FindIndex(ids, id => FacingOf(id) == wanted);
                if (index >= 0)
                    devices.Add(new CameraDevice(index, name, facing));
            }
            return devices;
        }
    }

    static CameraManager Manager =>
        (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService)!;

    partial void OpenPlatform(VideoSource source, CameraDevice device, int width, int height, int fps)
    {
        var ids = Manager.GetCameraIdList();
        if (device.Index >= ids.Length)
            throw new WebRtcException($"The camera \"{device.Name}\" is gone.");

        // A USB camera is mounted like a back one: its picture needs no mirroring.
        var facing = device.Facing == CameraFacing.Front ? LensFacing.Front : LensFacing.Back;
        var session = new Session(source, ids[device.Index], facing, width, height, fps);
        session.Open();
        _session = session;
    }

    partial void StopPlatform()
    {
        var session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();
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
        NativeCamera? _device;
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

        void OnOpened(NativeCamera device)
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

        sealed class DeviceCallback(Session session) : NativeCamera.StateCallback
        {
            public override void OnOpened(NativeCamera camera) => session.OnOpened(camera);
            public override void OnDisconnected(NativeCamera camera) => camera.Close();
            public override void OnError(NativeCamera camera, [GeneratedEnum] CameraError error) => camera.Close();
        }

        sealed class SessionCallback(Session owner, Surface surface) : CameraCaptureSession.StateCallback
        {
            public override void OnConfigured(CameraCaptureSession session) => owner.OnConfigured(session, surface);
            public override void OnConfigureFailed(CameraCaptureSession session) { }
        }
    }
}
