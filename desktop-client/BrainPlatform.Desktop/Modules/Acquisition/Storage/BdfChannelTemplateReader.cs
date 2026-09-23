using System.Globalization;
using System.IO;
using System.Text;

namespace BrainPlatform.Desktop.Acquisition.Storage;

/// <summary>
/// Reads BDF header labels only. It never opens or interprets EEG samples.
/// File channel position is preserved so a confirmed BDF layout can be applied
/// to the same amplifier's physical input positions.
/// </summary>
public sealed record BdfChannelTemplate(
    string SourcePath,
    int FileChannelCount,
    IReadOnlyDictionary<int, string> LabelsByChannelIndex);

public sealed class BdfChannelTemplateReader
{
    private const int FixedHeaderBytes = 256;
    private const int ChannelHeaderBytes = 256;
    private const int LabelWidth = 16;

    public BdfChannelTemplate Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("未找到可导入的 BDF 文件。", path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fixedHeader = ReadExactly(stream, FixedHeaderBytes);
        var headerBytes = ReadInteger(fixedHeader, 184, 8, "BDF 头长度");
        var channelCount = ReadInteger(fixedHeader, 252, 4, "BDF 通道数");
        var expectedHeaderBytes = checked(FixedHeaderBytes + channelCount * ChannelHeaderBytes);
        if (channelCount <= 0 || headerBytes != expectedHeaderBytes)
        {
            throw new InvalidDataException("BDF 头中的通道数或头长度不符合标准格式。");
        }

        var header = new byte[headerBytes];
        Buffer.BlockCopy(fixedHeader, 0, header, 0, FixedHeaderBytes);
        ReadExactly(stream, header, FixedHeaderBytes, headerBytes - FixedHeaderBytes);

        var labels = new Dictionary<int, string>();
        for (var index = 0; index < channelCount; index++)
        {
            var label = Encoding.ASCII.GetString(header, FixedHeaderBytes + index * LabelWidth, LabelWidth).Trim();
            if (!IsSignalLabel(label))
            {
                continue;
            }

            labels.Add(index, label);
        }

        return new BdfChannelTemplate(Path.GetFullPath(path), channelCount, labels);
    }

    private static bool IsSignalLabel(string label) =>
        !string.IsNullOrWhiteSpace(label) &&
        !label.Equals("null", StringComparison.OrdinalIgnoreCase) &&
        !label.Equals("trigger", StringComparison.OrdinalIgnoreCase) &&
        !label.Equals("status", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("annotations", StringComparison.OrdinalIgnoreCase);

    private static int ReadInteger(byte[] header, int offset, int length, string fieldName)
    {
        var value = Encoding.ASCII.GetString(header, offset, length).Trim();
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"{fieldName}无效。");
        }

        return parsed;
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        ReadExactly(stream, buffer, 0, count);
        return buffer;
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);
            if (read == 0)
            {
                throw new InvalidDataException("BDF 文件头不完整。");
            }

            offset += read;
            count -= read;
        }
    }
}
