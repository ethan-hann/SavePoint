using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Services.Storage;

public class DirectorySizeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_file_under_the_folder_is_added_up_at_any_depth()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("a.bin", 100);
        temp.WriteBytes("sub/b.bin", 250);
        temp.WriteBytes("sub/deeper/still/c.bin", 7);

        Assert.Equal(357, await DirectorySize.GetBytesAsync(temp.Path, Ct));
    }

    [Fact]
    public async Task An_empty_folder_is_zero_bytes()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Combine("empty"));

        Assert.Equal(0, await DirectorySize.GetBytesAsync(temp.Path, Ct));
    }

    [Fact]
    public async Task A_folder_that_does_not_exist_is_zero_bytes()
    {
        using var temp = new TempFolder();

        Assert.Equal(0, await DirectorySize.GetBytesAsync(temp.Combine("missing"), Ct));
    }

    [Fact]
    public async Task Hidden_files_and_hidden_folders_count()
    {
        using var temp = new TempFolder();
        var hiddenFile = temp.WriteBytes(".hidden.bin", 11);
        temp.WriteBytes(".git/objects/pack", 22);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hiddenFile, FileAttributes.Hidden);
            File.SetAttributes(temp.Combine(".git"), FileAttributes.Hidden | FileAttributes.Directory);
        }

        Assert.Equal(33, await DirectorySize.GetBytesAsync(temp.Path, Ct));
    }

    [Fact]
    public async Task A_link_to_a_folder_is_not_followed()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("tree/real.bin", 40);
        temp.WriteBytes("outside/big.bin", 5000);
        Assert.True(temp.TryCreateDirectoryLink(temp.Combine("tree", "link"), temp.Combine("outside")), "could not create a directory link on this machine");

        Assert.Equal(40, await DirectorySize.GetBytesAsync(temp.Combine("tree"), Ct));
    }

    [Fact]
    public async Task A_link_to_a_file_is_not_counted()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("tree/real.bin", 40);
        var target = temp.WriteBytes("outside/big.bin", 5000);
        Assert.SkipUnless(temp.TryCreateFileLink(temp.Combine("tree", "link.bin"), target), "this user may not create file links");

        Assert.Equal(40, await DirectorySize.GetBytesAsync(temp.Combine("tree"), Ct));
    }

    [Fact]
    public void A_file_that_vanishes_between_listing_and_measuring_is_skipped()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("keep.bin", 100);
        temp.WriteBytes("gone.bin", 900);

        var total = DirectorySize.Measure(
            temp.Path,
            file =>
            {
                if (file.Name == "gone.bin")
                {
                    File.Delete(file.FullName);
                    return new FileInfo(file.FullName).Length;
                }

                return file.Length;
            },
            Ct);

        Assert.Equal(100, total);
    }

    [Fact]
    public void A_folder_that_vanishes_mid_scan_is_skipped()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("trigger.bin", 10);
        temp.WriteBytes("sub/inside.bin", 500);

        var total = DirectorySize.Measure(
            temp.Path,
            file =>
            {
                if (file.Name == "trigger.bin")
                {
                    Directory.Delete(temp.Combine("sub"), recursive: true);
                }

                return file.Length;
            },
            Ct);

        Assert.Equal(10, total);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_stops_the_scan_before_it_starts()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("a.bin", 1);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DirectorySize.GetBytesAsync(temp.Path, cts.Token));
    }

    [Fact]
    public void Cancelling_during_the_scan_stops_it()
    {
        using var temp = new TempFolder();
        for (var i = 0; i < 50; i++)
        {
            temp.WriteBytes($"f{i}.bin", 1);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var measured = 0;

        Assert.ThrowsAny<OperationCanceledException>(() => DirectorySize.Measure(
            temp.Path,
            file =>
            {
                if (++measured == 3)
                {
                    cts.Cancel();
                }

                return file.Length;
            },
            cts.Token));

        Assert.True(measured < 50, $"the scan went on after the cancel and measured {measured} files");
    }
}
