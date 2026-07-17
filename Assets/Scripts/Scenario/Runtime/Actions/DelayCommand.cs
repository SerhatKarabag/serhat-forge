using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that waits for a specified duration before completing.
    /// </summary>
    [Serializable]
    public class DelayCommand : BaseCommand
    {
        [SerializeField]
        private float _secondsToWait = 1f;

        private bool _isCancelled;

        public float SecondsToWait
        {
            get => _secondsToWait;
            set => _secondsToWait = value;
        }

        public DelayCommand()
        {
        }

        public DelayCommand(float seconds)
        {
            _secondsToWait = seconds;
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            if (_secondsToWait <= 0)
                return;

            float elapsed = 0f;
            while (elapsed < _secondsToWait && !_isCancelled)
            {
                await Task.Yield();
                elapsed += Time.deltaTime;
            }
        }

        public override void Stop()
        {
            _isCancelled = true;
        }
    }
}
