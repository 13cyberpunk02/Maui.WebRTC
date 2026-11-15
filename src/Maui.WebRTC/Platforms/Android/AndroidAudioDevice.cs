using Android.Runtime;

namespace Maui.WebRTC;

/// <summary>
/// libwebrtc's Java audio device (org.webrtc.audio.JavaAudioDeviceModule), built with the options the app chose.
/// The library's own native default can't be configured and prefers the phone's built-in echo canceller,
/// which on many phones leaves an echo.
/// </summary>
sealed class AndroidAudioDevice : IDisposable
{
    const string ModuleClass = "org/webrtc/audio/JavaAudioDeviceModule";
    const string BuilderClass = "org/webrtc/audio/JavaAudioDeviceModule$Builder";
    const string BuilderType = "L" + BuilderClass + ";";

    nint _module;

    AndroidAudioDevice(nint module) => _module = module;

    /// <summary>The JNI global reference to hand to the native factory.</summary>
    public nint Handle => _module;

    public static AndroidAudioDevice Create(PeerConnectionFactoryOptions options)
    {
        var moduleClass = JNIEnv.FindClass(ModuleClass);
        var builderClass = JNIEnv.FindClass(BuilderClass);
        var builder = nint.Zero;
        try
        {
            var context = global::Android.App.Application.Context;
            var create = JNIEnv.GetStaticMethodID(moduleClass, "builder", $"(Landroid/content/Context;){BuilderType}");
            builder = JNIEnv.CallStaticObjectMethod(moduleClass, create, new JValue(context));
            builder = Set(builderClass, builder, "setUseHardwareAcousticEchoCanceler", options.UseHardwareEchoCanceller);
            builder = Set(builderClass, builder, "setUseHardwareNoiseSuppressor", options.UseHardwareNoiseSuppressor);

            var build = JNIEnv.GetMethodID(builderClass, "createAudioDeviceModule", $"()L{ModuleClass};");
            var module = JNIEnv.CallObjectMethod(builder, build);
            var global = JNIEnv.NewGlobalRef(module);
            JNIEnv.DeleteLocalRef(module);
            return new AndroidAudioDevice(global);
        }
        finally
        {
            if (builder != 0)
                JNIEnv.DeleteLocalRef(builder);
            JNIEnv.DeleteGlobalRef(builderClass);
            JNIEnv.DeleteGlobalRef(moduleClass);
        }
    }

    // The builder's setters return the builder; the previous local reference is dropped for the new one.
    static nint Set(nint builderClass, nint builder, string method, bool value)
    {
        var setter = JNIEnv.GetMethodID(builderClass, method, $"(Z){BuilderType}");
        var next = JNIEnv.CallObjectMethod(builder, setter, new JValue(value));
        JNIEnv.DeleteLocalRef(builder);
        return next;
    }

    public void Dispose()
    {
        var module = Interlocked.Exchange(ref _module, 0);
        if (module == 0)
            return;

        var moduleClass = JNIEnv.FindClass(ModuleClass);
        try
        {
            JNIEnv.CallVoidMethod(module, JNIEnv.GetMethodID(moduleClass, "release", "()V"));
        }
        finally
        {
            JNIEnv.DeleteGlobalRef(moduleClass);
            JNIEnv.DeleteGlobalRef(module);
        }
    }
}
