using UnityEngine;
using UnityEngine.InputSystem;

namespace RosSharp.RosBridgeClient
{
    public class FloatPublisher_GripperControl : UnityPublisher<MessageTypes.Std.Float64>
    {
        public InputActionReference openGripperAction;
        public InputActionReference closeGripperAction;
        public float adjustmentSpeed = 0.5f; // Speed at which the gripper value changes
        public bool debugMessage = false;

        private MessageTypes.Std.Float64 message;
        private float gripperValue = 0.0f; // Current gripper state (0 = closed, 1 = open)
        private bool isOpening = false;
        private bool isClosing = false;

        public float gripper_min = -0.366f;
        public float gripper_max = 0.638f;

        protected override void Start()
        {
            base.Start();
            InitializeMessage();
            
            openGripperAction.action.started += ctx => isOpening = true;
            openGripperAction.action.canceled += ctx => isOpening = false;

            closeGripperAction.action.started += ctx => isClosing = true;
            closeGripperAction.action.canceled += ctx => isClosing = false;
            gripperValue = 0.0f;
        }

        void Update()
        {
            if (isOpening)
            {
                gripperValue += adjustmentSpeed * Time.deltaTime;
                gripperValue = Mathf.Clamp(gripperValue, gripper_min, gripper_max); // Ensure value stays within bounds
            }

            if (isClosing)
            {
                gripperValue -= adjustmentSpeed * Time.deltaTime;
                gripperValue = Mathf.Clamp(gripperValue, gripper_min, gripper_max); // Ensure value stays within bounds
            }

            
             message.data = gripperValue;
             Publish(message);

            if (debugMessage)
            {
                Debug.Log($"Gripper Value: {gripperValue}");
            }
        }

        private void InitializeMessage()
        {
            message = new MessageTypes.Std.Float64();
        }

        private void OnDestroy()
        {
            // Unsubscribe from events to avoid memory leaks
            openGripperAction.action.started -= ctx => isOpening = true;
            openGripperAction.action.canceled -= ctx => isOpening = false;

            closeGripperAction.action.started -= ctx => isClosing = true;
            closeGripperAction.action.canceled -= ctx => isClosing = false;
        }
    }
}
