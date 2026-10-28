namespace MouseFix.Core;

/// <summary>One power setting of the game plan.</summary>
public sealed record PowerSetting(string Description, Guid Subgroup, Guid Setting, uint Value);

/// <summary>
/// L.T("Modo jogo", "Game mode"): a "MouseFix Jogo" power plan made from Windows' High performance plan and tuned for games
/// (CPU always at full speed, no core parking, aggressive boost, no USB/PCIe power saving, no sleep).
/// Activating remembers the plan that was active; deactivating goes back to it.
/// </summary>
public sealed class GameModeService
{
    public const string PlanName = "MouseFix Jogo";
    private static string PlanDescription => L.T("Criado pelo MouseFix: desempenho máximo para jogos e emuladores.", "Made by MouseFix: maximum performance for games and emulators.");

    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid SubUsb = new("2a737441-1930-4402-8d77-b2bebba308a3");
    private static readonly Guid SubPciExpress = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");

    /// <summary>The tuning applied to the game plan (checked on this PC with powercfg, no administrator needed).</summary>
    public static IReadOnlyList<PowerSetting> Settings =>
    [
        new(L.T("Processador mínimo em 100%", "Minimum processor at 100%"), SubProcessor, new("893dee8e-2bef-41e0-89c6-b55d0929964c"), 100),
        new(L.T("Processador máximo em 100%", "Maximum processor at 100%"), SubProcessor, new("bc5038f7-23e0-4960-96da-33abaf5935ec"), 100),
        new(L.T("Sem estacionamento de núcleos", "No core parking"), SubProcessor, new("0cc5b647-c1df-4637-891a-dec35c318583"), 100),
        new(L.T("Turbo agressivo", "Aggressive boost"), SubProcessor, new("be337238-0d82-4146-a960-4f3749d470c7"), 2),
        new(L.T("USB sem economia de energia", "USB without power saving"), SubUsb, new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), 0),
        new(L.T("PCIe sem economia de energia", "PCIe without power saving"), SubPciExpress, new("ee12f906-d277-404b-b6da-e5fa1a576df5"), 0),
        new(L.T("Disco nunca desliga", "Disk never turns off"), SubDisk, new("6738e2c4-e8a5-4a42-b16a-e040e769756e"), 0),
        new(L.T("PC nunca suspende", "PC never sleeps"), SubSleep, new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"), 0),
        new(L.T("PC nunca hiberna", "PC never hibernates"), SubSleep, new("9d7815a6-7ee4-497e-8888-515a05f02364"), 0),
    ];

    private readonly IPowerPlans _plans;
    private readonly AppSettings _settings;
    private readonly Action _save;

    public GameModeService(IPowerPlans plans, AppSettings settings, Action save)
    {
        _plans = plans;
        _settings = settings;
        _save = save;
    }

    /// <summary>
    /// The game plan if it exists in Windows. If MouseFix lost track of it (e.g. its settings file was reset),
    /// an existing plan named "MouseFix Jogo" is adopted instead of creating a duplicate.
    /// </summary>
    public Guid? GamePlan
    {
        get
        {
            if (Guid.TryParse(_settings.GamePowerPlan, out var plan) && _plans.Exists(plan))
                return plan;
            var existing = PlansNamedLikeOurs().Cast<Guid?>().FirstOrDefault();
            if (existing is Guid found)
            {
                _settings.GamePowerPlan = found.ToString();
                _save();
            }
            return existing;
        }
    }

    private IEnumerable<Guid> PlansNamedLikeOurs() => _plans.List().Where(p => _plans.GetName(p) == PlanName);

    public bool IsActive => GamePlan is Guid plan && _plans.GetActive() == plan;

    public string ActivePlanName => _plans.GetName(_plans.GetActive());

    /// <summary>Settings this PC doesn't have (e.g. no core parking control); filled by the last activation.</summary>
    public IReadOnlyList<string> Unsupported { get; private set; } = [];

    /// <summary>True when game mode was turned on because an emulator opened (so it turns off when the emulator closes).</summary>
    public bool ActivatedAutomatically => IsActive && _settings.GameModeAutomatic;

    /// <summary>Creates/updates the game plan and activates it. Returns null when verified, otherwise what failed.</summary>
    /// <param name="automatic">True when an emulator opening triggered it; false when the user clicked the button.</param>
    public string? Activate(bool automatic = false)
    {
        var active = _plans.GetActive();
        var plan = EnsurePlan();
        if (active != plan)
        {
            _settings.PowerPlanBeforeGame = active.ToString();
            _plans.SetActive(plan);
        }
        _settings.GameModeAutomatic = automatic;
        _save();
        return _plans.GetActive() == plan ? null : L.T("o Windows não ativou o plano MouseFix Jogo", "Windows didn't turn on the MouseFix Jogo plan");
    }

    /// <summary>
    /// Goes back to the plan that was active before game mode. Does nothing if the game plan isn't active
    /// (the user picked another plan meanwhile). Returns null when verified, otherwise what failed.
    /// </summary>
    public string? Deactivate()
    {
        if (!IsActive)
            return null;

        var previous = Guid.TryParse(_settings.PowerPlanBeforeGame, out var p) && _plans.Exists(p) && p != GamePlan
            ? p
            : _plans.Exists(Balanced) ? Balanced : HighPerformance;
        _plans.SetActive(previous);
        _settings.PowerPlanBeforeGame = null;
        _settings.GameModeAutomatic = false;
        _save();
        return _plans.GetActive() == previous ? null : L.T("o Windows não voltou ao plano anterior", "Windows didn't go back to the previous plan");
    }

    /// <summary>Used when uninstalling: leaves game mode and removes the game plan from Windows.</summary>
    public void RemovePlan()
    {
        Deactivate();
        // Every plan with our name, in case an older run left a duplicate behind.
        foreach (var plan in PlansNamedLikeOurs().ToList())
        {
            if (_plans.GetActive() == plan)
                _plans.SetActive(_plans.Exists(Balanced) ? Balanced : HighPerformance);
            _plans.Delete(plan);
        }
        _settings.GamePowerPlan = null;
        _settings.PowerPlanBeforeGame = null;
        _settings.GameModeAutomatic = false;
        _save();
    }

    private Guid EnsurePlan()
    {
        if (GamePlan is not Guid plan)
        {
            var source = _plans.Exists(HighPerformance) ? HighPerformance : _plans.GetActive();
            plan = _plans.Duplicate(source);
            _plans.Rename(plan, PlanName, PlanDescription);
            _settings.GamePowerPlan = plan.ToString();
            _save();
        }

        // Re-applied every time, so the plan is right even if someone edited it in the Control Panel.
        var unsupported = new List<string>();
        foreach (var s in Settings)
        {
            if (!_plans.TrySetValue(plan, s.Subgroup, s.Setting, s.Value) || _plans.ReadValue(plan, s.Subgroup, s.Setting) != s.Value)
                unsupported.Add(s.Description);
        }
        Unsupported = unsupported;
        return plan;
    }
}
