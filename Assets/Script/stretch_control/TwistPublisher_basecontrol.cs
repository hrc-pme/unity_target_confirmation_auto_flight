using UnityEngine;
using UnityEngine.InputSystem;

namespace RosSharp.RosBridgeClient
{
    public class TwistPublisher_basecontrol : UnityPublisher<MessageTypes.Geometry.Twist>
    {
        public InputActionReference base_contorl;
        
        [Header ("Set max speed in m/s")]
        public float max_speed = 1;

        public bool debug_message = false;
        private MessageTypes.Geometry.Twist message;


        protected override void Start()
        {
            base.Start();
            InitializeMessage();
            base_contorl.action.performed += UpdateMessage;
        }

        
        private void InitializeMessage()
        {
            message = new MessageTypes.Geometry.Twist();
            message.linear = new MessageTypes.Geometry.Vector3();
            message.angular = new MessageTypes.Geometry.Vector3();
        }

        private void UpdateMessage(InputAction.CallbackContext callback)
        {
            // Read joystick input
            Vector2 joystickValue = callback.ReadValue<Vector2>();
            if (debug_message){
                print($"leftthand stick value:{joystickValue}");
            }
            // Assign the joystick values to linear.x and angular.z
            message.linear.x = -joystickValue.y * max_speed;  // Forward/backward movement
            message.angular.z = joystickValue.x * max_speed; // Rotational movement

            Publish(message);
        }

        private void OnDestroy()
        {
            base_contorl.action.performed -= UpdateMessage;
        }
    }
}
