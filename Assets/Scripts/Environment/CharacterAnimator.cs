using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ClutchFPS.Environment
{
    /// Drives a humanoid character's locomotion animation from its movement
    /// speed, via Playables (no AnimatorController asset needed).
    ///
    /// Clips are optional: assign Humanoid idle/walk/run clips (e.g. from
    /// Mixamo — the Survivalist rig is Humanoid so they retarget) and they
    /// play automatically. With no clips assigned it falls back to a subtle
    /// procedural bob/lean so characters don't glide perfectly rigid.
    public class CharacterAnimator : MonoBehaviour
    {
        [Header("Humanoid clips (optional)")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip runClip;
        [SerializeField] private AnimationClip deathClip;
        [SerializeField] private float runSpeedThreshold = 3.5f;

        private bool _dead;

        /// Plays the death clip once and stops locomotion updates. Falls back
        /// to the caller's own handling if no death clip is assigned.
        public bool PlayDeath()
        {
            if (deathClip == null || _animator == null) return false;
            _dead = true;
            PlayClip(deathClip);
            return true;
        }

        public void ResetDeath()
        {
            _dead = false;
            _currentClip = null;
        }

        [Header("Procedural fallback")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private float bobHeight = 0.06f;
        [SerializeField] private float bobFrequency = 8f;
        [SerializeField] private float leanAngle = 4f;

        private Animator _animator;
        private PlayableGraph _graph;
        private AnimationClip _currentClip;
        private Vector3 _lastPosition;
        private float _speed;
        private float _bobTime;
        private Vector3 _visualBasePosition;
        private bool _hasClips;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            if (visualRoot == null && _animator != null) visualRoot = _animator.transform;
            if (visualRoot != null) _visualBasePosition = visualRoot.localPosition;
            _lastPosition = transform.position;
            _hasClips = idleClip != null || walkClip != null || runClip != null;
            if (_hasClips) UpdateClips();
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        private void Update()
        {
            // Measure actual world speed: works for NavMeshAgents and for
            // remote clients that only receive synced transforms.
            Vector3 delta = transform.position - _lastPosition;
            delta.y = 0f;
            float instantSpeed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, instantSpeed, 10f * Time.deltaTime);
            _lastPosition = transform.position;

            if (_dead) return;
            if (_hasClips) UpdateClips();
            else UpdateProcedural();
        }

        private void UpdateClips()
        {
            if (_animator == null) return;
            AnimationClip wanted = _speed < 0.15f ? idleClip
                : _speed >= runSpeedThreshold && runClip != null ? runClip
                : walkClip != null ? walkClip : idleClip;
            if (wanted == null || wanted == _currentClip) return;
            PlayClip(wanted);
        }

        private void PlayClip(AnimationClip clip)
        {
            if (_graph.IsValid()) _graph.Destroy();
            _graph = PlayableGraph.Create($"{name}-Locomotion");
            var output = AnimationPlayableOutput.Create(_graph, "Locomotion", _animator);
            var playable = AnimationClipPlayable.Create(_graph, clip);
            output.SetSourcePlayable(playable);
            _graph.Play();
            _currentClip = clip;
        }

        /// No clips: fake a stride with a vertical bob and a slight forward
        /// lean scaled by speed. Cheap, but reads as "moving" rather than
        /// "sliding".
        private void UpdateProcedural()
        {
            if (visualRoot == null) return;
            float moveAmount = Mathf.Clamp01(_speed / 3f);

            if (moveAmount > 0.05f)
            {
                _bobTime += Time.deltaTime * bobFrequency * moveAmount;
                float bob = Mathf.Abs(Mathf.Sin(_bobTime)) * bobHeight * moveAmount;
                visualRoot.localPosition = _visualBasePosition + Vector3.up * bob;
                visualRoot.localRotation = Quaternion.Euler(leanAngle * moveAmount, 0f, 0f);
            }
            else
            {
                visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, _visualBasePosition, 8f * Time.deltaTime);
                visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, Quaternion.identity, 8f * Time.deltaTime);
            }
        }
    }
}
