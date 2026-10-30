using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Savepoint.Core.Tests.Build;

/// <summary>A local stand-in for the Forgejo API and the GitHub API, so the release mirror script runs end to end without reaching either server.</summary>
internal sealed class MirrorStub : IDisposable
{
    internal const string ForgejoRepository = "forge-owner/forge-repo";
    internal const string GitHubRepository = "gh-owner/gh-repo";

    internal sealed record Asset(string Name, byte[] Bytes);

    internal sealed class ForgejoRelease
    {
        public long Id { get; init; }
        public required string Tag { get; init; }
        public string Name { get; set; } = "";
        public string Body { get; set; } = "";
        public bool Draft { get; init; }
        public bool Prerelease { get; init; }
        public DateTime Created { get; set; } = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        public List<Asset> Assets { get; } = [];
    }

    internal sealed class GitHubAsset
    {
        public long Id { get; init; }
        public required string Name { get; init; }
        public required byte[] Bytes { get; init; }
        public string? Digest { get; init; }
    }

    internal sealed class GitHubRelease
    {
        public long Id { get; init; }
        public required string Tag { get; init; }
        public string Name { get; set; } = "";
        public string Body { get; set; } = "";
        public bool Prerelease { get; set; }
        public bool Draft { get; set; }
        public List<GitHubAsset> Assets { get; } = [];
    }

    internal sealed record Request(string Method, string Path, string? Authorization);

    private readonly HttpListener _listener = new();
    private readonly Task _loop;
    private readonly object _gate = new();
    private long _nextId = 100;

    public MirrorStub()
    {
        // Port 0 is not available to HttpListener, so a free port is asked of the OS first.
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Origin = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Origin + "/");
        _listener.Start();
        _loop = Task.Run(Serve);
    }

    public string Origin { get; }
    public string ForgejoApi => Origin + "/forgejo/api/v1";
    public string ForgejoServer => Origin + "/forgejo";
    public string GitHubApi => Origin + "/gh";
    public string GitHubUploads => Origin + "/gh-up";

    public List<ForgejoRelease> ForgejoReleases { get; } = [];
    public List<GitHubRelease> GitHubReleases { get; } = [];
    public HashSet<string> GitHubTags { get; } = [];

    /// <summary>When false the GitHub asset objects carry no digest, as GitHub did before it reported them.</summary>
    public bool ReportDigests { get; set; } = true;

    /// <summary>When set, every digest GitHub reports for an upload is this value instead of the hash of the bytes it received.</summary>
    public string? DigestOverride { get; set; }

    public List<Request> Requests { get; } = [];

    public ForgejoRelease AddForgejoRelease(string tag, string name, string body, bool prerelease, bool draft = false, params Asset[] assets)
    {
        var release = new ForgejoRelease { Id = Interlocked.Increment(ref _nextId), Tag = tag, Name = name, Body = body, Prerelease = prerelease, Draft = draft };
        release.Assets.AddRange(assets);
        ForgejoReleases.Add(release);
        return release;
    }

    public GitHubRelease AddGitHubRelease(string tag, string name, string body, bool prerelease, params Asset[] assets)
    {
        var release = new GitHubRelease { Id = Interlocked.Increment(ref _nextId), Tag = tag, Name = name, Body = body, Prerelease = prerelease };
        foreach (var asset in assets)
        {
            release.Assets.Add(MakeAsset(asset.Name, asset.Bytes));
        }

        GitHubReleases.Add(release);
        GitHubTags.Add(tag);
        return release;
    }

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string SumsFor(params Asset[] assets) => string.Concat(assets.Select(asset => $"{Sha(asset.Bytes)}  {asset.Name}\n"));

    public Request[] RequestsTo(string prefix)
    {
        lock (_gate)
        {
            return Requests.Where(request => request.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }
    }

    private GitHubAsset MakeAsset(string name, byte[] bytes) => new()
    {
        Id = Interlocked.Increment(ref _nextId),
        Name = name,
        Bytes = bytes,
        Digest = ReportDigests ? "sha256:" + (DigestOverride ?? Sha(bytes)) : null,
    };

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                Handle(context);
            }
            catch (Exception ex)
            {
                Reply(context, 500, new { message = ex.ToString() });
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var path = request.Url!.AbsolutePath;
        lock (_gate)
        {
            Requests.Add(new Request(request.HttpMethod, path, request.Headers["Authorization"]));
        }

        using var buffer = new MemoryStream();
        request.InputStream.CopyTo(buffer);
        var body = buffer.ToArray();
        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);

        lock (_gate)
        {
            if (path.StartsWith("/forgejo/api/v1/", StringComparison.Ordinal))
            {
                HandleForgejo(context, path["/forgejo/api/v1".Length..], query);
            }
            else if (path.StartsWith("/forgejo/", StringComparison.Ordinal))
            {
                HandleForgejoDownload(context, path);
            }
            else if (path.StartsWith("/gh-up/", StringComparison.Ordinal))
            {
                HandleGitHubUpload(context, path["/gh-up".Length..], query, body);
            }
            else if (path.StartsWith("/gh/", StringComparison.Ordinal))
            {
                HandleGitHub(context, path["/gh".Length..], body);
            }
            else
            {
                Reply(context, 404, new { message = "unknown route" });
            }
        }
    }

    private object ForgejoJson(ForgejoRelease release) => new
    {
        id = release.Id,
        tag_name = release.Tag,
        name = release.Name,
        body = release.Body,
        draft = release.Draft,
        prerelease = release.Prerelease,
        created_at = release.Created.ToString("O"),
        assets = release.Assets.Select((asset, index) => new
        {
            id = release.Id * 1000 + index,
            name = asset.Name,
            size = asset.Bytes.Length,
            type = asset.Name.StartsWith("Source code", StringComparison.Ordinal) ? "external" : "attachment",
            browser_download_url = asset.Name.StartsWith("Source code", StringComparison.Ordinal)
                ? $"{ForgejoServer}/{ForgejoRepository}/releases/tag/git-sources-1"
                : $"{ForgejoServer}/{ForgejoRepository}/releases/download/{release.Tag}/{Uri.EscapeDataString(asset.Name)}",
        }).ToArray(),
    };

    private void HandleForgejo(HttpListenerContext context, string path, System.Collections.Specialized.NameValueCollection query)
    {
        var prefix = $"/repos/{ForgejoRepository}/releases";
        if (context.Request.HttpMethod != "GET" || !path.StartsWith(prefix, StringComparison.Ordinal))
        {
            Reply(context, 404, new { message = "unknown forgejo route" });
            return;
        }

        if (path == prefix)
        {
            // Without draft=true the list holds the published releases, as with a token that cannot see drafts.
            var drafts = query["draft"] == "true";
            var page = int.Parse(query["page"] ?? "1");
            var limit = int.Parse(query["limit"] ?? "50");
            var listed = ForgejoReleases.Where(release => release.Draft == drafts).OrderByDescending(release => release.Created).Skip((page - 1) * limit).Take(limit);
            Reply(context, 200, listed.Select(ForgejoJson).ToArray());
            return;
        }

        Reply(context, 404, new { message = "no such release" });
    }

    private void HandleForgejoDownload(HttpListenerContext context, string path)
    {
        var marker = $"/forgejo/{ForgejoRepository}/releases/download/";
        if (path.StartsWith(marker, StringComparison.Ordinal))
        {
            var parts = path[marker.Length..].Split('/', 2);
            var asset = ForgejoReleases.FirstOrDefault(release => release.Tag == parts[0])?.Assets.FirstOrDefault(a => a.Name == Uri.UnescapeDataString(parts[1]));
            if (asset is not null)
            {
                context.Response.StatusCode = 200;
                context.Response.OutputStream.Write(asset.Bytes);
                context.Response.Close();
                return;
            }
        }

        Reply(context, 404, new { message = "no such file" });
    }

    private object GitHubJson(GitHubRelease release) => new
    {
        id = release.Id,
        tag_name = release.Tag,
        name = release.Name,
        body = release.Body,
        draft = release.Draft,
        prerelease = release.Prerelease,
        assets = release.Assets.Select(asset => new { id = asset.Id, name = asset.Name, size = asset.Bytes.Length, state = "uploaded", digest = asset.Digest }).ToArray(),
    };

    private void HandleGitHub(HttpListenerContext context, string path, byte[] body)
    {
        var repo = $"/repos/{GitHubRepository}";
        var method = context.Request.HttpMethod;
        if (!path.StartsWith(repo + "/", StringComparison.Ordinal))
        {
            Reply(context, 404, new { message = "unknown github route" });
            return;
        }

        var rest = path[repo.Length..];
        if (method == "GET" && rest.StartsWith("/git/ref/tags/", StringComparison.Ordinal))
        {
            var tag = Uri.UnescapeDataString(rest["/git/ref/tags/".Length..]);
            Reply(context, GitHubTags.Contains(tag) ? 200 : 404, GitHubTags.Contains(tag) ? new { @ref = "refs/tags/" + tag } : new { message = "Not Found" });
        }
        else if (method == "GET" && rest.StartsWith("/releases/tags/", StringComparison.Ordinal))
        {
            var release = GitHubReleases.FirstOrDefault(r => r.Tag == Uri.UnescapeDataString(rest["/releases/tags/".Length..]));
            Reply(context, release is null ? 404 : 200, release is null ? new { message = "Not Found" } : GitHubJson(release));
        }
        else if (method == "GET" && rest.StartsWith("/releases/assets/", StringComparison.Ordinal))
        {
            var id = long.Parse(rest["/releases/assets/".Length..]);
            var asset = GitHubReleases.SelectMany(r => r.Assets).FirstOrDefault(a => a.Id == id);
            if (asset is null)
            {
                Reply(context, 404, new { message = "Not Found" });
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.OutputStream.Write(asset.Bytes);
            context.Response.Close();
        }
        else if (method == "POST" && rest == "/releases")
        {
            var json = JsonDocument.Parse(body).RootElement;
            var release = new GitHubRelease
            {
                Id = Interlocked.Increment(ref _nextId),
                Tag = json.GetProperty("tag_name").GetString()!,
                Name = json.GetProperty("name").GetString()!,
                Body = json.GetProperty("body").GetString()!,
                Prerelease = json.GetProperty("prerelease").GetBoolean(),
                Draft = json.GetProperty("draft").GetBoolean(),
            };
            GitHubReleases.Add(release);
            Reply(context, 201, GitHubJson(release));
        }
        else if (method == "PATCH" && rest.StartsWith("/releases/", StringComparison.Ordinal))
        {
            var release = GitHubReleases.First(r => r.Id == long.Parse(rest["/releases/".Length..]));
            var json = JsonDocument.Parse(body).RootElement;
            release.Name = json.GetProperty("name").GetString()!;
            release.Body = json.GetProperty("body").GetString()!;
            release.Prerelease = json.GetProperty("prerelease").GetBoolean();
            Reply(context, 200, GitHubJson(release));
        }
        else if (method == "DELETE" && rest.StartsWith("/releases/assets/", StringComparison.Ordinal))
        {
            var id = long.Parse(rest["/releases/assets/".Length..]);
            foreach (var release in GitHubReleases)
            {
                release.Assets.RemoveAll(asset => asset.Id == id);
            }

            Reply(context, 204, null);
        }
        else
        {
            Reply(context, 404, new { message = "unknown github route" });
        }
    }

    private void HandleGitHubUpload(HttpListenerContext context, string path, System.Collections.Specialized.NameValueCollection query, byte[] body)
    {
        var prefix = $"/repos/{GitHubRepository}/releases/";
        if (context.Request.HttpMethod != "POST" || !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith("/assets", StringComparison.Ordinal))
        {
            Reply(context, 404, new { message = "unknown upload route" });
            return;
        }

        var id = long.Parse(path[prefix.Length..^"/assets".Length]);
        var release = GitHubReleases.First(r => r.Id == id);
        var name = query["name"]!;
        if (release.Assets.Any(asset => asset.Name == name))
        {
            Reply(context, 422, new { message = "already_exists" });
            return;
        }

        var created = MakeAsset(name, body);
        release.Assets.Add(created);
        Reply(context, 201, new { id = created.Id, name = created.Name, size = created.Bytes.Length, state = "uploaded", digest = created.Digest });
    }

    private static void Reply(HttpListenerContext context, int status, object? payload)
    {
        context.Response.StatusCode = status;
        if (payload is not null)
        {
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(JsonSerializer.SerializeToUtf8Bytes(payload));
        }

        context.Response.Close();
    }

    public void Dispose()
    {
        _listener.Close();
        _loop.Wait(TimeSpan.FromSeconds(5));
    }
}
