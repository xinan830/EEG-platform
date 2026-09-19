using System.Runtime.InteropServices;
using System.IO;
using System.Text;

namespace BrainPlatform.Desktop.Acquisition.AntEego;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct AntEegoNativeAmplifierInfo
{
    public int Id;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Serial;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AntEegoNativeChannelInfo
{
    public int Index;
    public AntEegoNativeChannelType Type;
}

internal sealed class AntEegoNativeApi : IDisposable
{
    internal const int ExpectedSdkVersion = 57172;
    private const int ErrorBufferLength = 1024;
    private readonly nint libraryHandle;
    private readonly InitDelegate initialize;
    private readonly ExitDelegate exit;
    private readonly GetVersionDelegate getVersion;
    private readonly GetAmplifiersDelegate getAmplifiers;
    private readonly GetAmplifierTextDelegate getAmplifierType;
    private readonly OpenAmplifierDelegate openAmplifier;
    private readonly CloseAmplifierDelegate closeAmplifier;
    private readonly GetChannelsDelegate getAmplifierChannels;
    private readonly GetIntListDelegate getSamplingRates;
    private readonly GetDoubleListDelegate getReferenceRanges;
    private readonly GetDoubleListDelegate getBipolarRanges;
    private readonly OpenEegStreamDelegate openEegStream;
    private readonly CloseStreamDelegate closeStream;
    private readonly GetChannelsDelegate getStreamChannels;
    private readonly GetStreamChannelCountDelegate getStreamChannelCount;
    private readonly PrefetchDelegate prefetch;
    private readonly GetDataDelegate getData;
    private readonly GetErrorStringDelegate getErrorString;
    private bool disposed;

    private AntEegoNativeApi(nint libraryHandle)
    {
        this.libraryHandle = libraryHandle;
        initialize = Bind<InitDelegate>("eemagine_sdk_init");
        exit = Bind<ExitDelegate>("eemagine_sdk_exit");
        getVersion = Bind<GetVersionDelegate>("eemagine_sdk_get_version");
        getAmplifiers = Bind<GetAmplifiersDelegate>("eemagine_sdk_get_amplifiers_info");
        getAmplifierType = Bind<GetAmplifierTextDelegate>("eemagine_sdk_get_amplifier_type");
        openAmplifier = Bind<OpenAmplifierDelegate>("eemagine_sdk_open_amplifier");
        closeAmplifier = Bind<CloseAmplifierDelegate>("eemagine_sdk_close_amplifier");
        getAmplifierChannels = Bind<GetChannelsDelegate>("eemagine_sdk_get_amplifier_channel_list");
        getSamplingRates = Bind<GetIntListDelegate>("eemagine_sdk_get_amplifier_sampling_rates_available");
        getReferenceRanges = Bind<GetDoubleListDelegate>("eemagine_sdk_get_amplifier_reference_ranges_available");
        getBipolarRanges = Bind<GetDoubleListDelegate>("eemagine_sdk_get_amplifier_bipolar_ranges_available");
        openEegStream = Bind<OpenEegStreamDelegate>("eemagine_sdk_open_eeg_stream");
        closeStream = Bind<CloseStreamDelegate>("eemagine_sdk_close_stream");
        getStreamChannels = Bind<GetChannelsDelegate>("eemagine_sdk_get_stream_channel_list");
        getStreamChannelCount = Bind<GetStreamChannelCountDelegate>("eemagine_sdk_get_stream_channel_count");
        prefetch = Bind<PrefetchDelegate>("eemagine_sdk_prefetch");
        getData = Bind<GetDataDelegate>("eemagine_sdk_get_data");
        getErrorString = Bind<GetErrorStringDelegate>("eemagine_sdk_get_error_string");
    }

    public static AntEegoNativeApi Load(string sdkLibraryPath)
    {
        if (!Environment.Is64BitProcess)
        {
            throw new AntEegoSdkException("ANT_EEGO_X64_REQUIRED", "ANT/eego acquisition requires an x64 desktop process.");
        }

        var fullPath = Path.GetFullPath(sdkLibraryPath);
        if (!File.Exists(fullPath))
        {
            throw new AntEegoSdkException("ANT_EEGO_SDK_NOT_FOUND", $"eego SDK DLL was not found: {fullPath}");
        }

        nint handle = 0;
        try
        {
            handle = NativeLibrary.Load(fullPath);
            var api = new AntEegoNativeApi(handle);
            var reportedVersion = api.getVersion();
            if (reportedVersion != ExpectedSdkVersion)
            {
                api.Dispose();
                throw new AntEegoSdkException(
                    "ANT_EEGO_SDK_VERSION_UNSUPPORTED",
                    $"Expected eego SDK version {ExpectedSdkVersion}, but the loaded DLL reported {reportedVersion}.");
            }

            api.initialize();
            return api;
        }
        catch (AntEegoSdkException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (handle != 0)
            {
                NativeLibrary.Free(handle);
            }

            throw new AntEegoSdkException("ANT_EEGO_SDK_LOAD_FAILED", exception.Message);
        }
    }

    public IReadOnlyList<AntEegoNativeAmplifierInfo> GetAmplifiers()
    {
        var values = new AntEegoNativeAmplifierInfo[64];
        var count = EnsureSuccess(getAmplifiers(values, values.Length), "get amplifier list");
        return CopyValues(values, count, "amplifier list");
    }

    public string GetAmplifierType(int amplifierId) =>
        GetAmplifierText(getAmplifierType, amplifierId, "get amplifier type");

    public IReadOnlyList<AntEegoNativeChannelInfo> GetAmplifierChannels(int amplifierId) =>
        GetChannels(getAmplifierChannels, amplifierId, "get amplifier channel list");

    public IReadOnlyList<AntEegoNativeChannelInfo> GetStreamChannels(int streamId) =>
        GetChannels(getStreamChannels, streamId, "get stream channel list");

    public IReadOnlyList<int> GetSamplingRates(int amplifierId) =>
        GetIntValues(getSamplingRates, amplifierId, "get sampling rates");

    public IReadOnlyList<double> GetReferenceRanges(int amplifierId) =>
        GetDoubleValues(getReferenceRanges, amplifierId, "get reference ranges");

    public IReadOnlyList<double> GetBipolarRanges(int amplifierId) =>
        GetDoubleValues(getBipolarRanges, amplifierId, "get bipolar ranges");

    public void OpenAmplifier(int amplifierId) =>
        EnsureSuccess(openAmplifier(amplifierId), "open amplifier");

    public void CloseAmplifier(int amplifierId) =>
        EnsureSuccess(closeAmplifier(amplifierId), "close amplifier");

    public int OpenEegStream(
        int amplifierId,
        int samplingRateHz,
        double referenceRangeVolts,
        double bipolarRangeVolts,
        AntEegoNativeChannelInfo[] channels) =>
        EnsureSuccess(
            openEegStream(amplifierId, samplingRateHz, referenceRangeVolts, bipolarRangeVolts, channels, channels.Length),
            "open EEG stream");

    public void CloseStream(int streamId) =>
        EnsureSuccess(closeStream(streamId), "close EEG stream");

    public int GetStreamChannelCount(int streamId) =>
        EnsureSuccess(getStreamChannelCount(streamId), "get stream channel count");

    public double[] ReadData(int streamId, int byteCount)
    {
        if (byteCount <= 0 || byteCount % sizeof(double) != 0)
        {
            throw new AntEegoSdkException("ANT_EEGO_DATA_ALIGNMENT_INVALID", "SDK prefetch returned an invalid data byte count.");
        }

        var values = new double[byteCount / sizeof(double)];
        var buffer = Marshal.AllocHGlobal(byteCount);
        try
        {
            EnsureSuccess(getData(streamId, buffer, byteCount), "read EEG stream data");
            Marshal.Copy(buffer, values, 0, values.Length);
            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public int Prefetch(int streamId) => EnsureSuccess(prefetch(streamId), "prefetch EEG stream data");

    public double[]? ReadAvailableData(int streamId)
    {
        var byteCount = Prefetch(streamId);
        return byteCount == 0 ? null : ReadData(streamId, byteCount);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            exit();
        }
        finally
        {
            NativeLibrary.Free(libraryHandle);
        }
    }

    private IReadOnlyList<AntEegoNativeChannelInfo> GetChannels(
        GetChannelsDelegate operation,
        int handle,
        string operationName)
    {
        var values = new AntEegoNativeChannelInfo[1024];
        var count = EnsureSuccess(operation(handle, values, values.Length), operationName);
        return CopyValues(values, count, operationName);
    }

    private IReadOnlyList<int> GetIntValues(GetIntListDelegate operation, int amplifierId, string operationName)
    {
        var values = new int[1024];
        var count = EnsureSuccess(operation(amplifierId, values, values.Length), operationName);
        return CopyValues(values, count, operationName);
    }

    private IReadOnlyList<double> GetDoubleValues(GetDoubleListDelegate operation, int amplifierId, string operationName)
    {
        var values = new double[1024];
        var count = EnsureSuccess(operation(amplifierId, values, values.Length), operationName);
        return CopyValues(values, count, operationName);
    }

    private string GetAmplifierText(GetAmplifierTextDelegate operation, int amplifierId, string operationName)
    {
        var buffer = new byte[256];
        EnsureSuccess(operation(amplifierId, buffer, buffer.Length), operationName);
        return Encoding.ASCII.GetString(buffer).TrimEnd('\0').Trim();
    }

    private int EnsureSuccess(int result, string operationName)
    {
        if (result >= 0)
        {
            return result;
        }

        throw new AntEegoSdkException(
            $"ANT_EEGO_NATIVE_{result}",
            $"ANT/eego SDK failed to {operationName}: {GetErrorDetail()} (code {result}).");
    }

    private string GetErrorDetail()
    {
        var buffer = Marshal.AllocHGlobal(ErrorBufferLength);
        try
        {
            _ = getErrorString(buffer, ErrorBufferLength);
            return Marshal.PtrToStringAnsi(buffer) ?? "No vendor error detail was returned.";
        }
        catch
        {
            return "No vendor error detail was available.";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private TDelegate Bind<TDelegate>(string exportName) where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(NativeLibrary.GetExport(libraryHandle, exportName));

    private static IReadOnlyList<T> CopyValues<T>(T[] values, int count, string operationName)
    {
        if (count > values.Length)
        {
            throw new AntEegoSdkException(
                "ANT_EEGO_NATIVE_CAPACITY_EXCEEDED",
                $"ANT/eego SDK returned {count} entries for {operationName}, exceeding adapter capacity {values.Length}.");
        }

        return values.Take(count).ToArray();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ExitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetAmplifiersDelegate([In, Out] AntEegoNativeAmplifierInfo[] values, int valueCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetAmplifierTextDelegate(int amplifierId, [Out] byte[] value, int valueSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenAmplifierDelegate(int amplifierId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CloseAmplifierDelegate(int amplifierId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetChannelsDelegate(int handle, [In, Out] AntEegoNativeChannelInfo[] values, int valueCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetIntListDelegate(int amplifierId, [In, Out] int[] values, int valueCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDoubleListDelegate(int amplifierId, [In, Out] double[] values, int valueCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenEegStreamDelegate(
        int amplifierId,
        int samplingRateHz,
        double referenceRangeVolts,
        double bipolarRangeVolts,
        [In] AntEegoNativeChannelInfo[] channels,
        int channelCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CloseStreamDelegate(int streamId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetStreamChannelCountDelegate(int streamId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PrefetchDelegate(int streamId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDataDelegate(int streamId, nint buffer, int bufferSizeInBytes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetErrorStringDelegate(nint buffer, int bufferSize);
}
