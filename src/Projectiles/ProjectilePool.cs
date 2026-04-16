// src/Projectiles/ProjectilePool.cs
// ─────────────────────────────────────────────────────────────────────────────
// Per-type object pool for all projectile scenes — Godot Node singleton.
//
// Placed in Level01.tscn (NOT an autoload) so its lifetime matches the level.
// Access via the static Instance property, which is set in _Ready().
//
// Scene tree location:
//   Level01 → ProjectilePool (this node)
//     └── Pool_PlasmaBolt (Node2D)
//     └── Pool_Missile    (Node2D)
//     └── ...             (one container per ProjectileType)
//
// Wire-up in the editor:
//   1. Add a ProjectilePool node to Level01.tscn.
//   2. In the Inspector, set ProjectileScenes array length to 8.
//   3. Assign each slot in enum index order:
//        [0] PlasmaBolt.tscn   [1] Missile.tscn   [2] PlasmaBlob.tscn
//        [3] CrystalSpine.tscn [4] OrganicSpore.tscn
//        [5] ConstitutionBlast.tscn  [6] FoxBroadcastPulse.tscn
//        [7] HateShuriken.tscn
//
// Per-type pool sizes are all set to PoolSizePerType (default 20), which is
// conservative.  Refer to Section 8 of the arch doc for per-type size rationale
// — override by adding individual [Export] overrides per type if needed.
//
// N-002 compliance:
//   Get() and Return() perform zero heap allocations in the hot path.
//   Queue<T>.Enqueue / TryDequeue are O(1) amortised and allocation-free
//   after the initial capacity is reached (pre-allocated in _Ready).
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

// ── Projectile type enum ──────────────────────────────────────────────────────
// Values are used as indices into the ProjectileScenes export array.
// DO NOT reorder without updating the editor Inspector assignments.

public enum ProjectileType
{
    PlasmaBolt        = 0,
    Missile           = 1,
    PlasmaBlob        = 2,
    CrystalSpine      = 3,
    OrganicSpore      = 4,
    ConstitutionBlast = 5,
    FoxBroadcastPulse = 6,
    HateShuriken      = 7,
}

// ── Pool ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages pre-allocated <see cref="Projectile"/> instances for all projectile
/// types.  Callers acquire an instance with <see cref="Get"/> and return it
/// with <see cref="Return"/> (typically called by the projectile itself on
/// despawn — see <c>Projectile.ReturnToPool()</c>).
/// </summary>
public partial class ProjectilePool : Node
{
    // ── Singleton accessor ───────────────────────────────────────────────────

    /// <summary>
    /// Set in <c>_Ready()</c>.  Valid for the lifetime of Level01.tscn.
    /// All callers should null-check if they can run outside the level scene.
    /// </summary>
    public static ProjectilePool Instance { get; private set; } = null!;

    // ── Exports (set in Godot Inspector) ─────────────────────────────────────

    /// <summary>
    /// One scene per <see cref="ProjectileType"/>, in enum-value order.
    /// Each scene root must extend <see cref="Projectile"/>.
    /// </summary>
    [Export] public PackedScene[] ProjectileScenes { get; set; } = System.Array.Empty<PackedScene>();

    /// <summary>
    /// Number of instances to pre-allocate per type.  20 is a safe default;
    /// increase individual types via per-type overrides if pool warnings appear.
    /// </summary>
    [Export] public int PoolSizePerType { get; set; } = 20;

    // ── Private state ────────────────────────────────────────────────────────

    private readonly Dictionary<ProjectileType, Queue<Projectile>> _pools     = new();
    private readonly Dictionary<ProjectileType, Node2D>            _containers = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        foreach (ProjectileType type in System.Enum.GetValues<ProjectileType>())
        {
            int index = (int)type;

            // Guard: scene must be assigned in the Inspector.
            if (index >= ProjectileScenes.Length || ProjectileScenes[index] is null)
            {
                GD.PushWarning(
                    $"ProjectilePool: No PackedScene assigned for {type} " +
                    $"(index {index}). Assign it in the Inspector.");
                _pools[type] = new Queue<Projectile>(0);
                continue;
            }

            // Create an organisational container for this type's instances.
            var container = new Node2D { Name = $"Pool_{type}" };
            AddChild(container);
            _containers[type] = container;

            // Pre-allocate.
            var queue = new Queue<Projectile>(PoolSizePerType);
            for (int i = 0; i < PoolSizePerType; i++)
            {
                var p = ProjectileScenes[index].Instantiate<Projectile>();
                p.PoolType = type;

                // Start fully dormant.
                p.Visible = false;
                p.SetProcess(false);
                p.SetPhysicsProcess(false);
                p.Monitoring    = false;
                p.Monitorable   = false;

                container.AddChild(p);
                queue.Enqueue(p);
            }

            _pools[type] = queue;
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieve a dormant projectile from the pool, position and activate it.
    /// </summary>
    /// <param name="type">Which projectile type to spawn.</param>
    /// <param name="position">World-space spawn position.</param>
    /// <param name="velocity">Initial velocity in pixels per second.</param>
    /// <returns>
    /// A live <see cref="Projectile"/>, or <c>null</c> if the pool for this
    /// type is exhausted.  Callers must null-check the return value.
    /// </returns>
    public Projectile? Get(ProjectileType type, Vector2 position, Vector2 velocity)
    {
        if (!_pools.TryGetValue(type, out var queue) || !queue.TryDequeue(out var p))
        {
            GD.PushWarning(
                $"ProjectilePool: Pool exhausted for {type}. " +
                $"Consider increasing PoolSizePerType (currently {PoolSizePerType}).");
            return null;
        }

        // Configure before enabling so no physics callbacks fire mid-setup.
        p.GlobalPosition = position;
        p.Velocity       = velocity;
        p.OnActivated(); // reset _returned guard before re-enabling

        p.Visible           = true;
        p.Monitoring        = true;
        p.Monitorable       = true;
        p.SetProcess(true);
        p.SetPhysicsProcess(true);

        return p;
    }

    /// <summary>
    /// Return a projectile to the pool.  Called by <see cref="Projectile.ReturnToPool"/>.
    /// After this call the projectile is dormant — do not hold any references to it.
    /// </summary>
    /// <param name="p">The projectile to recycle.  Must have been obtained from this pool.</param>
    public void Return(Projectile p)
    {
        // Disable everything before re-enqueueing so no callbacks fire while dormant.
        p.Visible           = false;
        p.SetProcess(false);
        p.SetPhysicsProcess(false);
        p.Monitoring        = false;
        p.Monitorable       = false;

        if (_pools.TryGetValue(p.PoolType, out var queue))
        {
            queue.Enqueue(p);
        }
        else
        {
            GD.PushError(
                $"ProjectilePool.Return: Projectile '{p.Name}' has unknown " +
                $"PoolType {p.PoolType}. This is a bug — was it instantiated " +
                $"outside the pool?");
        }
    }
}
