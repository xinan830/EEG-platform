using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class RecordingSubjectMetadataTests
{
    [Theory]
    [InlineData("", "", "未关联")]
    [InlineData("S001", "", "S001")]
    [InlineData("", "姓名", "姓名")]
    [InlineData("S001", "姓名", "S001 · 姓名")]
    public void SubjectDisplaySupportsEitherField(string code, string name, string expected) =>
        Assert.Equal(expected, new RecordingSubject(code, name).DisplayText);

    [Fact]
    public async Task SavePreservesExistingNameAndUnknownMetadataAndSupportsClearing()
    {
        var directory = Directory.CreateTempSubdirectory("subject-metadata-").FullName;
        try
        {
            var path = Path.Combine(directory, "recording-display.json");
            await File.WriteAllTextAsync(path, "{\"displayName\":\"record\",\"other\":42}");
            await RecordingMetadataStore.SaveAsync(directory, null, new(" S001 ", " 姓名 "));
            Assert.Equal(new RecordingSubject("S001", "姓名"), RecordingMetadataStore.ReadSubject(directory));
            using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
            {
                Assert.Equal("record", json.RootElement.GetProperty("displayName").GetString());
                Assert.Equal(42, json.RootElement.GetProperty("other").GetInt32());
            }
            await RecordingMetadataStore.SaveAsync(directory, "renamed", new());
            Assert.Equal("未关联", RecordingMetadataStore.ReadSubject(directory).DisplayText);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            await File.WriteAllTextAsync(path, "broken-json");
            await Assert.ThrowsAnyAsync<JsonException>(() => RecordingMetadataStore.SaveAsync(directory, "x", new()));
            Assert.Equal("broken-json", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidOrCancelledMetadataSaveRetainsPreviousFile()
    {
        var directory = Directory.CreateTempSubdirectory("subject-save-failure-").FullName;
        try
        {
            await RecordingMetadataStore.SaveAsync(directory, "record", new("S001", "姓名"));
            var path = Path.Combine(directory, "recording-display.json");
            var before = await File.ReadAllBytesAsync(path);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordingMetadataStore.SaveAsync(directory, "new", new(new string('x', 161), "")));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                RecordingMetadataStore.SaveAsync(directory, "new", new(), cancellation.Token));
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
