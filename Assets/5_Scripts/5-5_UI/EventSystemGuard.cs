using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps one shared EventSystem alive across scene and dimension switches.
/// </summary>
public static class EventSystemGuard
{
    private const string CanonicalName = "EventSystem";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallbacks()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAfterSceneLoad()
    {
        EnsureExactlyOne();
    }

    public static EventSystem EnsureExactlyOne(bool createIfMissing = true)
    {
        EventSystem[] systems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
        EventSystem primary = SelectPrimary(systems);

        if (primary == null)
        {
            if (!createIfMissing)
                return null;

            GameObject eventSystemObject = new GameObject(
                CanonicalName,
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
            primary = eventSystemObject.GetComponent<EventSystem>();
            systems = new[] { primary };
        }

        if (!primary.gameObject.activeSelf)
            primary.gameObject.SetActive(true);
        if (!primary.enabled)
            primary.enabled = true;

        BaseInputModule primaryInputModule = primary.GetComponent<BaseInputModule>();
        if (primaryInputModule == null)
            primaryInputModule = primary.gameObject.AddComponent<InputSystemUIInputModule>();
        primaryInputModule.enabled = true;

        EventSystem.current = primary;

        foreach (EventSystem system in systems)
        {
            if (system == null || system == primary)
                continue;

            DisableAndDestroy(system);
        }

        return primary;
    }

    private static EventSystem SelectPrimary(EventSystem[] systems)
    {
        if (systems == null || systems.Length == 0)
            return null;

        EventSystem current = EventSystem.current;
        EventSystem canonical = Array.Find(systems, system =>
            system != null &&
            system.isActiveAndEnabled &&
            string.Equals(system.gameObject.name, CanonicalName, StringComparison.Ordinal));
        if (canonical != null)
            return canonical;

        if (current != null && current.isActiveAndEnabled)
            return current;

        EventSystem active = Array.Find(systems, system => system != null && system.isActiveAndEnabled);
        return active ?? Array.Find(systems, system => system != null);
    }

    private static void DisableAndDestroy(EventSystem duplicate)
    {
        foreach (BaseInputModule inputModule in duplicate.GetComponents<BaseInputModule>())
            inputModule.enabled = false;
        duplicate.enabled = false;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(duplicate.gameObject);
        else
            UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
    }

    private static void OnActiveSceneChanged(Scene previous, Scene next)
    {
        EnsureExactlyOne();
    }
}
