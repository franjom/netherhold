using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The entire game. Deterministic, headless, serialisable, Unity-free.
///
/// Contract, and the whole point of this type:
/// <para>
/// <b>Same database + same initial world + same seed + same input frames, in
/// the same order, produce the same <see cref="StateHash"/> at every tick — on
/// any machine, in any process.</b>
/// </para>
/// Enforced by <c>DeterminismTests</c> and by the CI gate. Anything that would
/// break it (wall-clock time, unordered collections, floating point in gameplay
/// maths, <see cref="object.GetHashCode"/> over strings, ambient randomness) is
/// a defect, not a trade-off.
/// </summary>
public sealed class GameSimulation
{
    private const ulong HashVersion = 3;

    private readonly List<ISimulationSystem> _systems;
    private readonly SimulationContext _context;

    public GameSimulation(
        GameDatabase database,
        WorldState world,
        ulong seed,
        IEnumerable<ISimulationSystem>? systems = null)
        : this(database, world, new SimRandom(seed), systems)
    {
    }

    public GameSimulation(
        GameDatabase database,
        WorldState world,
        SimRandom rng,
        IEnumerable<ISimulationSystem>? systems = null)
    {
        Database = database;
        World = world;
        Rng = rng;
        _systems = systems is null ? [] : [.. systems];
        _context = new SimulationContext(database, world, rng, NullEventSink.Instance);
    }

    public GameDatabase Database { get; }
    public WorldState World { get; }
    public SimRandom Rng { get; }

    /// <summary>
    /// Where to send observation events. Assigning a recorder must not change
    /// behaviour — see <see cref="SimEvent"/>.
    /// </summary>
    public ISimulationEventSink Events => _context.Events;

    public IReadOnlyList<ISimulationSystem> Systems => _systems;

    /// <summary>The next tick to be executed.</summary>
    public GameTick CurrentTick => World.CurrentTick;

    /// <summary>Starts sending observation events to <paramref name="events"/>. Behaviour-neutral.</summary>
    public void ObserveWith(ISimulationEventSink events) => _context.Events = events;

    /// <summary>
    /// Advances exactly one game tick. The ONLY way simulation time moves.
    ///
    /// Systems execute in registration order for tick N, then the world's tick
    /// becomes N+1. (Our convention, not a claim about the original's internal
    /// ordering, which is UNKNOWN — see <see cref="ISimulationSystem"/>.)
    /// </summary>
    public void Tick(in InputFrame input)
    {
        _context.Tick = World.CurrentTick;
        _context.Input = input;

        foreach (ISimulationSystem system in _systems)
            system.Execute(_context);

        World.CurrentTick = World.CurrentTick.Next;
    }

    public void Tick() => Tick(InputFrame.Empty);

    /// <summary>Runs <paramref name="ticks"/> ticks with no input.</summary>
    public void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++) Tick();
    }

    /// <summary>
    /// Structural hash of all simulation state: the world, and the RNG's
    /// position in its stream. Two runs that agree on this at every tick are
    /// behaviourally identical.
    ///
    /// The RNG state is included deliberately. Two runs can hold identical
    /// world state while having consumed different numbers of random draws;
    /// without this they would look identical now and diverge later, which is
    /// exactly the failure the gate exists to catch early.
    /// </summary>
    public ulong StateHash()
    {
        Hash64 hash = Hash64.Start();
        hash.Add(HashVersion);
        World.HashInto(ref hash);

        (ulong s0, ulong s1, ulong s2, ulong s3) = Rng.SaveState();
        hash.Add(s0);
        hash.Add(s1);
        hash.Add(s2);
        hash.Add(s3);

        return hash.Value;
    }
}
