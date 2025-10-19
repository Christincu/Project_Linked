using UnityEngine;

public enum InteractState { Click, Grib, Push }

public interface IInteractable
{
    InteractState CallState { get; }
    void Interact(GameObject actor);
}
