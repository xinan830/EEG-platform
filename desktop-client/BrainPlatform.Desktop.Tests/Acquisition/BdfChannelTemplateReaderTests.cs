using System.Text;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Storage;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class BdfChannelTemplateReaderTests
{
    [Fact]
    public void Reader_UsesOnlySignalLabelsAndPreservesTheirFilePositions()
    {
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bdf");
        WriteBdfHeader(file, ["Fp1", "F3", "Null", "Trigger", "BDF Annotations", "O2"]);

        var template = new BdfChannelTemplateReader().Read(file);

        Assert.Equal(6, template.FileChannelCount);
        Assert.Equal("Fp1", template.LabelsByChannelIndex[0]);
        Assert.Equal("F3", template.LabelsByChannelIndex[1]);
        Assert.Equal("O2", template.LabelsByChannelIndex[5]);
        Assert.DoesNotContain(2, template.LabelsByChannelIndex.Keys);
        Assert.DoesNotContain(3, template.LabelsByChannelIndex.Keys);
        Assert.DoesNotContain(4, template.LabelsByChannelIndex.Keys);
    }

    [Fact]
    public void Mapping_UsesMatchingPhysicalIndexesAndClearsOldLabelsForNullSlots()
    {
        var viewModel = new ChannelMappingViewModel(new ChannelLabelMappingStore(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));
        viewModel.LoadDevice(new AcquisitionDeviceDescriptor(
            "device",
            "device",
            "serial",
            [1000],
            ChannelCapabilities:
            [
                new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannelCapability(1, AcquisitionChannelKind.Reference, "V"),
            ]));
        viewModel.Rows[1].ElectrodeLabel = "OldLabel";

        viewModel.ApplyTemplate(new BdfChannelTemplate("template.bdf", 2, new Dictionary<int, string> { [0] = "Fp1" }));

        Assert.Equal("Fp1", viewModel.Rows[0].ElectrodeLabel);
        Assert.Equal(string.Empty, viewModel.Rows[1].ElectrodeLabel);
    }

    [Fact]
    public void Mapping_DefaultTemplateSeparatesActualInputsFromNamedAndSelectedDisplayElectrodes()
    {
        var mappingStore = new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));
        var displayStore = new ChannelDisplaySelectionStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));
        var viewModel = new ChannelMappingViewModel(mappingStore, displaySelectionStore: displayStore);
        var capabilities = Enumerable.Range(0, 24)
            .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Reference, "V"))
            .Concat(Enumerable.Range(24, 4).Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Bipolar, "V")))
            .ToArray();

        viewModel.LoadDevice(new AcquisitionDeviceDescriptor("device", "device", "serial", [500], ChannelCapabilities: capabilities));

        Assert.Equal(28, viewModel.ActualEegInputCount);
        Assert.Equal(21, viewModel.NamedElectrodeCount);
        Assert.Equal(21, viewModel.SelectedDisplayChannelCount);
        Assert.All(viewModel.DisplayRows, row => Assert.True(row.IsSelectedForDisplay));
        Assert.DoesNotContain(viewModel.DisplayRows, row => row.NativeChannelIndex == 20);
    }

    [Fact]
    public async Task Mapping_EmptyLegacyDisplaySelectionFallsBackToTheDefaultTemplate()
    {
        var mappingStore = new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));
        var displayStore = new ChannelDisplaySelectionStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));
        await displayStore.SaveAsync("serial", [], CancellationToken.None);
        var viewModel = new ChannelMappingViewModel(mappingStore, displaySelectionStore: displayStore);
        var capabilities = Enumerable.Range(0, 24)
            .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Reference, "V"))
            .Concat(Enumerable.Range(24, 4).Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Bipolar, "V")))
            .ToArray();

        viewModel.LoadDevice(new AcquisitionDeviceDescriptor("device", "device", "serial", [500], ChannelCapabilities: capabilities));

        Assert.Equal(21, viewModel.SelectedDisplayChannelCount);
    }

    private static void WriteBdfHeader(string path, IReadOnlyList<string> labels)
    {
        var bytes = new byte[256 + labels.Count * 256];
        Array.Fill(bytes, (byte)' ');
        WriteField(bytes, 184, 8, bytes.Length.ToString());
        WriteField(bytes, 252, 4, labels.Count.ToString());
        for (var index = 0; index < labels.Count; index++)
        {
            WriteField(bytes, 256 + index * 16, 16, labels[index]);
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void WriteField(byte[] target, int offset, int length, string value)
    {
        Encoding.ASCII.GetBytes(value).AsSpan(0, Math.Min(value.Length, length)).CopyTo(target.AsSpan(offset, length));
    }
}
