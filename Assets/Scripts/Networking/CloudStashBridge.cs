using System.Collections.Generic;
using System.Threading.Tasks;
using ClutchFPS.Core;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Bridges StashService to Unity Cloud Save. On sign-in it loads the
    /// account's stash blob (migrating a local stash the first time), then
    /// flushes changes back on a debounce. Self-bootstraps so no scene wiring
    /// is needed. Only the signed-in player's own stash is cloud-backed.
    public class CloudStashBridge : MonoBehaviour
    {
        private const string Key = "stash";
        private const float FlushInterval = 3f;

        private string _loadedForPlayer;
        private bool _busy;
        private float _nextFlush;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<CloudStashBridge>() != null) return;
            var go = new GameObject("CloudStashBridge");
            go.AddComponent<CloudStashBridge>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            if (!AccountService.IsSignedIn)
            {
                if (_loadedForPlayer != null)
                {
                    // Flush any unsaved changes before dropping the account.
                    if (StashService.CloudDirty)
                    {
                        string json = StashService.SnapshotCloudJson();
                        if (json != null) _ = SaveAsync(json);
                    }
                    StashService.DeactivateCloud();
                    _loadedForPlayer = null;
                }
                return;
            }

            // A newly signed-in (or switched) account: load its cloud stash.
            if (_loadedForPlayer != AccountService.PlayerId && !_busy)
            {
                LoadFor(AccountService.PlayerId, AccountService.DisplayName);
                return;
            }

            // Debounced flush of in-memory changes.
            if (_loadedForPlayer != null && !_busy
                && StashService.CloudDirty && Time.time >= _nextFlush)
            {
                _nextFlush = Time.time + FlushInterval;
                Flush();
            }
        }

        private async void LoadFor(string playerId, string displayName)
        {
            _busy = true;
            try
            {
                string json = await LoadAsync();
                StashService.ActivateCloud(displayName, json);
                _loadedForPlayer = playerId;
                // A migration (or first starter kit) leaves it dirty — persist now.
                if (StashService.CloudDirty)
                {
                    await SaveAsync(StashService.SnapshotCloudJson());
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CloudStash load failed: {e.Message}");
            }
            finally { _busy = false; }
        }

        private async void Flush()
        {
            _busy = true;
            string json = StashService.SnapshotCloudJson();
            try
            {
                if (json != null) await SaveAsync(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CloudStash save failed: {e.Message}");
                StashService.MarkCloudDirty(); // retry on the next tick
            }
            finally { _busy = false; }
        }

        private void OnApplicationQuit()
        {
            // Best-effort final flush; the app may close before it completes.
            if (_loadedForPlayer != null && StashService.CloudDirty)
            {
                string json = StashService.SnapshotCloudJson();
                if (json != null) _ = SaveAsync(json);
            }
        }

        private static async Task<string> LoadAsync()
        {
            Dictionary<string, Item> results =
                await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { Key });
            return results.TryGetValue(Key, out var item) ? item.Value.GetAs<string>() : null;
        }

        private static async Task SaveAsync(string json)
        {
            await CloudSaveService.Instance.Data.Player.SaveAsync(
                new Dictionary<string, object> { { Key, json } });
        }
    }
}
