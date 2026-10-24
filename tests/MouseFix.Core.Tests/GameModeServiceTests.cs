using MouseFix.Core;

namespace MouseFix.Core.Tests;

public class GameModeServiceTests
{
    private readonly FakePowerPlans _plans = new();
    private readonly AppSettings _settings = new();
    private int _saves;

    private GameModeService Service() => new(_plans, _settings, () => _saves++);

    [Fact]
    public void Activating_creates_the_game_plan_from_high_performance_tunes_it_and_switches_to_it()
    {
        _plans.Active = GameModeService.Balanced;
        var service = Service();

        Assert.Null(service.Activate());

        var plan = Assert.IsType<Guid>(service.GamePlan);
        Assert.Equal(GameModeService.HighPerformance, _plans.DuplicatedFrom[plan]);
        Assert.Equal(GameModeService.PlanName, _plans.Names[plan]);
        Assert.Equal(plan, _plans.Active);
        Assert.True(service.IsActive);
        Assert.All(GameModeService.Settings, s => Assert.Equal(s.Value, _plans.ReadValue(plan, s.Subgroup, s.Setting)));
        Assert.Equal(GameModeService.Balanced.ToString(), _settings.PowerPlanBeforeGame);
    }

    [Fact]
    public void Deactivating_goes_back_to_the_plan_that_was_active_before()
    {
        _plans.Active = GameModeService.HighPerformance;
        var service = Service();
        service.Activate();

        Assert.Null(service.Deactivate());

        Assert.Equal(GameModeService.HighPerformance, _plans.Active);
        Assert.False(service.IsActive);
        Assert.Null(_settings.PowerPlanBeforeGame);
    }

    [Fact]
    public void The_plan_is_created_once_and_reused()
    {
        var service = Service();
        service.Activate();
        service.Deactivate();
        service.Activate();

        Assert.Single(_plans.DuplicatedFrom);
    }

    [Fact]
    public void If_the_user_picked_another_plan_meanwhile_deactivating_leaves_it_alone()
    {
        var service = Service();
        service.Activate();
        var userChoice = Guid.NewGuid();
        _plans.Names[userChoice] = "Meu plano";
        _plans.Active = userChoice;

        service.Deactivate();

        Assert.Equal(userChoice, _plans.Active);
    }

    [Fact]
    public void A_game_plan_deleted_outside_mousefix_is_created_again()
    {
        var service = Service();
        service.Activate();
        var first = service.GamePlan!.Value;
        service.Deactivate();
        _plans.Delete(first);

        service.Activate();

        Assert.NotEqual(first, service.GamePlan);
        Assert.Equal(2, _plans.DuplicatedFrom.Count);
    }

    [Fact]
    public void Settings_this_pc_does_not_have_are_reported_but_do_not_fail_activation()
    {
        var parking = GameModeService.Settings.Single(s => s.Description.Contains("estacionamento"));
        _plans.Missing.Add(parking.Setting);
        var service = Service();

        Assert.Null(service.Activate());
        Assert.Equal([parking.Description], service.Unsupported);
    }

    [Fact]
    public void Automatic_activation_is_remembered_until_game_mode_turns_off()
    {
        var service = Service();

        service.Activate(automatic: true);
        Assert.True(service.ActivatedAutomatically);
        Assert.True(_settings.GameModeAutomatic);

        service.Deactivate();
        Assert.False(service.ActivatedAutomatically);
        Assert.False(_settings.GameModeAutomatic);

        service.Activate();
        Assert.False(service.ActivatedAutomatically);
    }

    [Fact]
    public void A_game_plan_whose_id_was_lost_is_adopted_instead_of_duplicated()
    {
        var service = Service();
        service.Activate();
        var plan = service.GamePlan!.Value;
        service.Deactivate();
        _settings.GamePowerPlan = null; // e.g. settings file reset

        service.Activate();

        Assert.Equal(plan, service.GamePlan);
        Assert.Single(_plans.DuplicatedFrom);
    }

    [Fact]
    public void Removing_deletes_every_plan_with_our_name()
    {
        var service = Service();
        service.Activate();
        var duplicate = Guid.NewGuid();
        _plans.Names[duplicate] = GameModeService.PlanName;
        _plans.Active = duplicate;

        service.RemovePlan();

        Assert.DoesNotContain(_plans.Names.Values, n => n == GameModeService.PlanName);
        Assert.Equal(GameModeService.Balanced, _plans.Active);
    }

    [Fact]
    public void Removing_the_plan_goes_back_and_deletes_it()
    {
        _plans.Active = GameModeService.Balanced;
        var service = Service();
        service.Activate();
        var plan = service.GamePlan!.Value;

        service.RemovePlan();

        Assert.Equal(GameModeService.Balanced, _plans.Active);
        Assert.False(_plans.Exists(plan));
        Assert.Null(_settings.GamePowerPlan);
    }

    private sealed class FakePowerPlans : IPowerPlans
    {
        public Dictionary<Guid, string> Names { get; } = new()
        {
            [GameModeService.Balanced] = "Equilibrado",
            [GameModeService.HighPerformance] = "Alto desempenho",
        };
        public Dictionary<Guid, Guid> DuplicatedFrom { get; } = [];
        public HashSet<Guid> Missing { get; } = [];
        private readonly Dictionary<(Guid, Guid, Guid), uint> _values = [];

        public Guid Active { get; set; } = GameModeService.Balanced;

        public IReadOnlyList<Guid> List() => Names.Keys.ToList();

        public Guid GetActive() => Active;

        public void SetActive(Guid plan) => Active = Exists(plan) ? plan : throw new InvalidOperationException();

        public bool Exists(Guid plan) => Names.ContainsKey(plan);

        public string GetName(Guid plan) => Names.GetValueOrDefault(plan, "?");

        public Guid Duplicate(Guid source)
        {
            var plan = Guid.NewGuid();
            Names[plan] = Names[source];
            DuplicatedFrom[plan] = source;
            return plan;
        }

        public void Rename(Guid plan, string name, string description) => Names[plan] = name;

        public bool TrySetValue(Guid plan, Guid subgroup, Guid setting, uint value)
        {
            if (Missing.Contains(setting))
                return false;
            _values[(plan, subgroup, setting)] = value;
            return true;
        }

        public uint? ReadValue(Guid plan, Guid subgroup, Guid setting) =>
            _values.TryGetValue((plan, subgroup, setting), out var v) ? v : null;

        public void Delete(Guid plan) => Names.Remove(plan);
    }
}
