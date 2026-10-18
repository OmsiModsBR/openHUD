using System.Diagnostics;
using OpenHud.Bridge;
using OpenHud.Scene;

namespace OpenHud.Maps;

// Fonts and pictures wait here until a tick has time to hand them to the native layer: a picture is up
// to a megabyte of base64, so a tick sends a few and leaves the rest for the next one.
internal sealed class UploadQueue
{
    public const double TickBudgetMilliseconds = 6;

    private readonly Queue<Upload> pending = new();
    private readonly HashSet<string> images = new(StringComparer.Ordinal);

    public int Pending => pending.Count;
    public long Sent { get; private set; }
    public long Failed { get; private set; }
    public int Fonts { get; private set; }
    public IReadOnlyCollection<string> ImageIds => images;
    public string? LastProblem { get; private set; }

    // A new map's package replaces the previous one's pictures: what is registered and not wanted again is dropped first.
    public void Replace(IReadOnlyList<Upload> next)
    {
        pending.Clear();
        var wanted = new HashSet<string>(next.OfType<ImageUpload>().Select(x => x.Id), StringComparer.Ordinal);
        foreach (var id in images.Where(id => !wanted.Contains(id)).ToList()) pending.Enqueue(new ImageDrop(id));
        foreach (var upload in next) pending.Enqueue(upload);
    }

    // Sends until the budget is spent (at least one item per call). Returns true when a font was registered.
    public bool Drain(NativeLink native)
    {
        var fontRegistered = false;
        var clock = Stopwatch.StartNew();
        while (pending.Count > 0)
        {
            var upload = pending.Dequeue();
            NativeAnswer answer;
            try
            {
                switch (upload)
                {
                    case FontUpload font:
                        answer = native.Call("font.put", SceneOps.FontPut(font.Role, font.Path));
                        if (answer.Ok) { Fonts++; fontRegistered = true; }
                        break;
                    case ImageUpload image:
                        answer = native.Call("image.put", image.Payload);
                        if (answer.Ok) images.Add(image.Id);
                        break;
                    case ImageDrop drop:
                        answer = native.Call("image.drop", SceneOps.ImageDrop(drop.Id));
                        images.Remove(drop.Id);
                        break;
                    default: continue;
                }
            }
            catch (SceneException exception)
            {
                answer = NativeAnswer.Failed("refused", exception.Message);
            }
            if (answer.Ok) Sent++;
            else
            {
                Failed++;
                LastProblem = upload.GetType().Name + ": " + NativeLink.Describe(answer);
            }
            if (clock.Elapsed.TotalMilliseconds >= TickBudgetMilliseconds) break;
        }
        return fontRegistered;
    }
}
