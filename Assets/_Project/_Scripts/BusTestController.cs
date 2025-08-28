using Cysharp.Threading.Tasks;
using Frost9.EventBus;
using R3;
using System;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

// Entry point driven by VContainer (no need for a MonoBehaviour)
// - IStartable.Start() logs the menu
// - ITickable.Tick() polls keys 1–9 each frame
public sealed class BusTestController : IStartable, ITickable, IDisposable
{
    private readonly Frost9.EventBus.IEventBus _bus;

    // Subscriptions we manage
    private IDisposable _healthSub;
    private IDisposable _deathSub;
    private IDisposable _deathRuleSub; // rule: on Health<=0, publish PlayerDied

    // For awaiting Next<HealthChanged> with cancel
    private CancellationTokenSource _nextCts;

    // Random health values when publishing
    private readonly System.Random _rng = new();

    // Color helpers
    private static string C(string hex, string msg) => $"<color=#{hex}>{msg}</color>";
    private static void Log(string hex, string msg) => Debug.Log(C(hex, msg));
    private static void Warn(string msg) => Debug.LogWarning(C("FFA500", msg)); // orange
    private static void Err(string msg) => Debug.LogError(C("FF5555", msg));    // red-ish

    public BusTestController(Frost9.EventBus.IEventBus bus)
    {
        _bus = bus;
    }

    public void Start()
    {
        ShowMenu();
    }

    public void Tick()
    {
        // 1: Subscribe to HealthChanged
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (_healthSub != null)
            {
                Warn("Already subscribed to HealthChanged.");
            }
            else
            {
                _healthSub = _bus.Observe<HealthChanged>()
                    .SubscribeSafe(e => Log("00FF7F", $"[SUB] HealthChanged received: {e.Value}"));

                Log("00FF7F", "Subscribed to HealthChanged.");
            }
        }

        // 2: Unsubscribe from HealthChanged
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            _healthSub?.Dispose();
            _healthSub = null;
            Log("FF77FF", "Unsubscribed from HealthChanged.");
        }

        // 3: Publish random HealthChanged (10..100)
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            var v = _rng.Next(10, 101);
            _bus.Publish(new HealthChanged(v));
            Log("FFFF66", $"[PUB] Published HealthChanged({v}).");
        }

        // 4: Publish HealthChanged(0) to simulate death condition
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            _bus.Publish(new HealthChanged(0));
            Log("FFFF66", "[PUB] Published HealthChanged(0).");
        }

        // 5: Start awaiting Next<HealthChanged> where Value <= 0
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            CancelNextAwaitIfAny();
            _nextCts = new CancellationTokenSource();
            _ = AwaitZeroHealthAsync(_nextCts.Token); // fire-and-forget test routine
            Log("00FFFF", "[AWAIT] Waiting for next HealthChanged where Value <= 0 (press 6 to cancel)...");
        }

        // 6: Cancel the await
        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            CancelNextAwaitIfAny();
            Log("AAAAAA", "[AWAIT] Canceled waiting.");
        }

        // 7: Subscribe to PlayerDied (and toggle)
        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            if (_deathSub == null)
            {
                _deathSub = _bus.Observe<PlayerDied>()
                    .SubscribeSafe(e => Log("FF66FF", $"[SUB] PlayerDied received: {e.Reason}"));
                Log("FF66FF", "Subscribed to PlayerDied.");
            }
            else
            {
                _deathSub.Dispose();
                _deathSub = null;
                Log("FF66FF", "Unsubscribed from PlayerDied.");
            }
        }

        // 8: Toggle rule: when Health<=0, publish PlayerDied("Health reached zero")
        if (Input.GetKeyDown(KeyCode.Alpha8))
        {
            if (_deathRuleSub == null)
            {
                _deathRuleSub = _bus.Observe<HealthChanged>()
                    .Where(e => e.Value <= 0)
                    .SubscribeSafe(_ =>
                    {
                        _bus.Publish(new PlayerDied("Health reached zero"));
                        Log("66CCFF", "[RULE] Auto-published PlayerDied due to Health<=0.");
                    });
                Log("66CCFF", "[RULE] Enabled: Health<=0 -> Publish PlayerDied.");
            }
            else
            {
                _deathRuleSub.Dispose();
                _deathRuleSub = null;
                Log("66CCFF", "[RULE] Disabled: Health<=0 -> Publish PlayerDied.");
            }
        }

        // 9: Publish from a background thread (should be ignored with a warning in dev/editor)
        if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            new System.Threading.Thread(() =>
            {
                try
                {
                    _bus.Publish(new HealthChanged(-5));
                }
                catch (Exception ex)
                {
                    // Shouldn't throw (your bus no-ops off-main-thread in release and warns in dev)
                    Err($"Background publish threw: {ex}");
                }
            }).Start();

            Log("AAAAAA", "[BG] Attempted Publish(HealthChanged(-5)) from a background thread (expect a warning/ignore).");
        }

        // 0 (zero): Show menu
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            ShowMenu();
        }
    }

    public void Dispose()
    {
        CancelNextAwaitIfAny();
        _healthSub?.Dispose();
        _deathSub?.Dispose();
        _deathRuleSub?.Dispose();
    }

    private async UniTaskVoid AwaitZeroHealthAsync(CancellationToken ct)
    {
        try
        {
            var evt = await _bus.Next<HealthChanged>(e => e.Value <= 0, ct);
            Log("00FFFF", $"[AWAIT] Next matched: {evt}");
        }
        catch (OperationCanceledException)
        {
            Log("AAAAAA", "[AWAIT] Canceled.");
        }
        catch (ObjectDisposedException)
        {
            Err("[AWAIT] Bus disposed while awaiting.");
        }
        catch (Exception ex)
        {
            Err($"[AWAIT] Faulted: {ex.Message}");
        }
    }

    private void CancelNextAwaitIfAny()
    {
        if (_nextCts != null)
        {
            _nextCts.Cancel();
            _nextCts.Dispose();
            _nextCts = null;
        }
    }

    private void ShowMenu()
    {
        Debug.Log(@"
<color=#87CEFA>=== EventBus Test Controller (keys 0-9) ===</color>
<color=#FFFFFF>1</color> <color=#00FF7F>Subscribe</color> to HealthChanged
<color=#FFFFFF>2</color> <color=#FF77FF>Unsubscribe</color> from HealthChanged
<color=#FFFFFF>3</color> <color=#FFFF66>Publish</color> random HealthChanged(10..100)
<color=#FFFFFF>4</color> <color=#FFFF66>Publish</color> HealthChanged(0)
<color=#FFFFFF>5</color> <color=#00FFFF>Await Next</color> HealthChanged where Value <= 0
<color=#FFFFFF>6</color> <color=#AAAAAA>Cancel Await</color>
<color=#FFFFFF>7</color> <color=#FF66FF>Toggle Subscribe</color> to PlayerDied
<color=#FFFFFF>8</color> <color=#66CCFF>Toggle Rule</color>: Health<=0 → Publish PlayerDied
<color=#FFFFFF>9</color> <color=#AAAAAA>Publish from background thread</color> (should be ignored w/ warning)
<color=#FFFFFF>0</color> Show this menu
");
    }

    public readonly struct HealthChanged
    {
        public readonly int Value;
        public HealthChanged(int value) => Value = value;
        public override string ToString() => $"HealthChanged({Value})";
    }

    public readonly struct PlayerDied
    {
        public readonly string Reason;
        public PlayerDied(string reason) => Reason = reason;
        public override string ToString() => $"PlayerDied(\"{Reason}\")";
    }
}
