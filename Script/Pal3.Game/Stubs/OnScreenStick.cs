namespace UnityEngine.InputSystem.OnScreen;

/// <summary>
/// Adapter for the RelativePositionWithStaticOrigin mode used by PAL3's Unity prefab.
/// The host supplies pointer positions in UI units with Y pointing up.
/// </summary>
public class OnScreenStick : Component
{
    private int? _pointerId;
    private Vector2 _pointerDownPosition;

    public float movementRange = 50f;
    public bool useIsometricView;
    public Vector2 value { get; private set; }
    public Vector2 Displacement { get; private set; }

    public void BeginInteraction(int pointerId, Vector2 pointerPosition)
    {
        if (_pointerId.HasValue) return;
        _pointerId = pointerId;
        _pointerDownPosition = pointerPosition;
    }

    public void MoveStick(int pointerId, Vector2 pointerPosition)
    {
        if (_pointerId != pointerId) return;
        if (!float.IsFinite(movementRange) || movementRange <= 0f)
        {
            CancelInteraction();
            return;
        }
        Vector2 delta = pointerPosition - _pointerDownPosition;
        Displacement = delta.sqrMagnitude > movementRange * movementRange
            ? delta.normalized * movementRange
            : delta;
        value = Displacement / movementRange;
    }

    public void EndInteraction(int pointerId)
    {
        if (_pointerId == pointerId) CancelInteraction();
    }

    public void CancelInteraction()
    {
        _pointerId = null;
        Displacement = Vector2.zero;
        value = Vector2.zero;
    }
}
