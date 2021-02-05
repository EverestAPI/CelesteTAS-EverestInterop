using Monocle;

namespace TAS.EverestInterop {
public class RemoveSelfComponent : Component {
    public RemoveSelfComponent() : base(true, false) { }

    public override void Added(Entity entity) {
        base.Added(entity);
        entity.Visible = false;
        entity.Collidable = false;
        entity.Collider = null;
    }

    public override void Update() {
        Entity?.RemoveSelf();
        RemoveSelf();
    }
}
}