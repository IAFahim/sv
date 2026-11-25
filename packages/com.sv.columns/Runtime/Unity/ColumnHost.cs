using System;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Sv.Columns.Unity
{
    [DisallowMultipleComponent]
    public sealed class ColumnHost : MonoBehaviour
    {
        public enum StepPhase
        {
            LateUpdate,
            FixedUpdate,
            Manual,
        }

        [SerializeField] TextAsset? pack = null;
        [SerializeField, Min(1)] int capacity = 65536;
        [SerializeField, Min(1)] int writers = 1;
        [SerializeField, Min(0)] int resolveParts = 0;
        [SerializeField] bool useJobs = true;
        [SerializeField] StepPhase stepIn = StepPhase.LateUpdate;

        ColumnStepper? stepper;

        public CompiledPack? Pack { get; private set; }

        public ColumnWorld? World { get; private set; }

        public StepPhase StepIn
        {
            get => stepIn;
            set => stepIn = value;
        }

        public event Action<ColumnHost>? Stepped;

        public void Open(string json, int capacity, int writers = 1, int resolveParts = 0, bool useJobs = true, KernelChoice kernels = KernelChoice.Best)
        {
            Close();
            var compiled = PackJson.Read(json).Compile();
            try
            {
                var parts = resolveParts > 0 ? resolveParts : useJobs ? (JobsUtility.JobWorkerCount + 1) * 4 : 1;
                World = new ColumnWorld(compiled, capacity, writers, parts, kernels);
            }
            catch
            {
                compiled.Dispose();
                throw;
            }

            Pack = compiled;
            stepper = useJobs ? new ColumnStepper(World) : null;
        }

        public void Step()
        {
            var world = World ?? throw new InvalidOperationException("this host has no open world; assign a pack or call Open");
            if (stepper != null)
            {
                stepper.Step();
            }
            else
            {
                world.Step();
            }

            Stepped?.Invoke(this);
        }

        public void Close()
        {
            stepper?.Dispose();
            stepper = null;
            World?.Dispose();
            World = null;
            Pack?.Dispose();
            Pack = null;
        }

        void Awake()
        {
            if (pack == null)
            {
                return;
            }

            try
            {
                Open(pack.text, capacity, writers, resolveParts, useJobs);
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
            }
        }

        void LateUpdate()
        {
            if (stepIn == StepPhase.LateUpdate && World != null)
            {
                Step();
            }
        }

        void FixedUpdate()
        {
            if (stepIn == StepPhase.FixedUpdate && World != null)
            {
                Step();
            }
        }

        void OnDestroy() => Close();
    }
}
