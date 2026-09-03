# Unity UAV Target Confirmation and Autonomous Flight

A Unity-based operator dashboard for reviewing UAV target candidates, confirming a selected target, and commanding the aircraft to approach the confirmed position. The final command height is fixed at **20 metres above the target**.

The client receives annotated camera images, target detections, flight telemetry, and command status messages through ROS bridge. It also places a 3D UAV model on a Cesium globe using live geographic coordinates.

> [!CAUTION]
> This project can publish commands that affect a physical UAV. Validate the full workflow in simulation, keep a manual takeover path available, configure geofencing and failsafes, and comply with local aviation and site-safety rules before conducting a real flight.

## Features

- Displays a live annotated camera stream in the Unity UI
- Receives target candidates with track IDs, classes, confidence scores, GPS coordinates, position error, and preview images
- Lets the operator move between multiple target candidates
- Enables confirmation only when the ROS-side safety and validation conditions are satisfied
- Publishes target-confirmation and cancellation commands
- Tracks command states such as accepted, executing, arrived, rejected, and failed
- Displays UAV GPS position, relative height, flight mode, armed state, satellite count, and positioning accuracy
- Updates a Cesium-anchored 3D UAV model from telemetry and optional IMU orientation
- Includes a mock test component for validating the UI without a live ROS connection

## System Architecture

```text
Camera / detector / flight controller
                 |
                 v
             ROS bridge
                 |
      +----------+-----------+
      |          |           |
      v          v           v
 Live image   Candidate    UAV telemetry
              JSON data      and IMU
      |          |           |
      +----------+-----------+
                 |
                 v
        Unity operator dashboard
                 |
       Select and confirm target
                 |
                 v
       Target command and status
                 |
                 v
    UAV approaches target at 20 m
```

## Repository Contents

This repository contains the three Unity project directories required to reproduce the editor project:

```text
.
├── Assets/            # Scenes, scripts, models, plug-ins, and Unity metadata
├── Packages/          # Unity Package Manager manifest and lock file
├── ProjectSettings/   # Unity editor and project configuration
├── .gitignore
└── README.md
```

Generated local directories such as `Library/`, `Temp/`, `Logs/`, `Obj/`, `Build/`, and `UserSettings/` are intentionally excluded.

## Requirements

- Unity **2022.3.51f1 LTS**
- A ROS environment providing rosbridge WebSocket connectivity
- The UAV target-detection and flight-control bridge that implements the topics described below
- Git, with Git LFS recommended for future additions of large binary assets

The package manifest currently includes these major dependencies:

- Cesium for Unity `1.23.3`
- Siemens ROS#
- Unity Robotics ROS-TCP-Connector
- TextMeshPro `3.0.7`
- XR Interaction Toolkit `2.6.3`
- OpenXR `1.13.2`
- XR Hands `1.4.3`
- Meta XR SDK `69.0.1`
- Animation Rigging `1.2.1`

## Quick Start

1. Clone the repository:

   ```bash
   git clone https://github.com/Xin-Chun-1122/unity_target_confirmation_auto_flight.git
   cd unity_target_confirmation_auto_flight
   ```

2. In Unity Hub, choose **Add project from disk** and select the cloned repository.

3. Open it with **Unity 2022.3.51f1** and allow Unity Package Manager to restore the packages in `Packages/manifest.json`.

4. Open the primary scene:

   ```text
   Assets/Scenes/Image_Process.unity
   ```

5. Configure the ROS bridge endpoint. The UAV dashboard currently enforces the following default in `Assets/Script/UAVDashboard/TargetCandidateData.cs`:

   ```text
   ws://10.0.0.8:9090
   ```

   Change `UavDashboardRos.OrinRosBridgeUrl` if the ROS computer uses a different address.

6. Start rosbridge, the target-detection node, and the UAV bridge, then enter Play mode in Unity.

`Assets/Scenes/Image_Process.unity` is already registered as the default build scene.

## ROS Topics

| Topic | Message type | Direction | Purpose |
|---|---|---|---|
| `/d455i/color/image_annotated/compressed` | `sensor_msgs/CompressedImage` | ROS to Unity | Live annotated camera image |
| `/unity/detections_json` | `std_msgs/String` (JSON) | ROS to Unity | Target candidates and confirmation readiness |
| `/uav/telemetry_json` | `std_msgs/String` (JSON) | ROS to Unity | GPS, altitude, flight mode, armed state, and accuracy |
| `/uav/imu` | `sensor_msgs/Imu` | ROS to Unity | Optional UAV orientation |
| `/unity/target_command` | `std_msgs/String` (JSON) | Unity to ROS | Target confirmation or cancellation command |
| `/unity/target_command_status` | `std_msgs/String` (JSON) | ROS to Unity | Command execution status |

These are the script defaults. Topic names can be adjusted in the corresponding Unity components when required.

## Operator Workflow

1. Wait for the camera, UAV telemetry, and candidate feeds to become active.
2. Review each candidate's image, track ID, target class, coordinates, confidence score, and position error.
3. Use the previous and next controls to select the intended candidate.
4. Confirm only after the interface reports that the candidate is ready and no confirmation blockers remain.
5. Monitor the returned command state while the UAV approaches the target.
6. Use the cancel control if the operation must be stopped.

`TargetCommandPublisher` forces `hoverHeightRelativeM` to `20.0`, and the external UAV bridge must independently enforce the same final-height constraint.

## Key Scripts

| Path | Responsibility |
|---|---|
| `Assets/Script/UAVDashboard/CompressedImageUIReceiver.cs` | Receives compressed images and updates the live `RawImage` |
| `Assets/Script/UAVDashboard/TargetCandidateData.cs` | Defines message data models and the shared rosbridge endpoint |
| `Assets/Script/UAVDashboard/TargetCandidateSubscriber.cs` | Subscribes to and parses candidate JSON packets |
| `Assets/Script/UAVDashboard/TargetSelectionUIController.cs` | Controls candidate navigation, readiness, previews, and operator actions |
| `Assets/Script/UAVDashboard/TargetCommandPublisher.cs` | Publishes confirm/cancel commands and tracks command responses |
| `Assets/Script/UAVDashboard/UavRosBridgeStateReceiver.cs` | Receives telemetry and IMU data and updates the Cesium UAV model |
| `Assets/Script/UAVDashboard/TargetCandidateMockTester.cs` | Injects local candidates and command states for UI testing |

## Testing Without ROS

Add `TargetCandidateMockTester` to a scene object and assign its subscriber and publisher references. The default test keys are:

| Key | Action |
|---|---|
| `F6` | Inject mock candidates |
| `F7` | Simulate an accepted command |
| `F8` | Simulate command execution |
| `F9` | Simulate arrival at the target |
| `F10` | Simulate a rejected command |
| `F11` | Clear the mock data |

## Troubleshooting

### The Unity project opens with missing packages

- Confirm that Git is installed and available to Unity Package Manager.
- Open **Window > Package Manager** and allow Git-based packages to finish resolving.
- Verify that `Packages/manifest.json` and `Packages/packages-lock.json` are present.

### The dashboard does not receive ROS data

- Confirm that rosbridge is running and reachable on port `9090`.
- Verify the address in `UavDashboardRos.OrinRosBridgeUrl`.
- Confirm that the topic names and ROS message types match the table above.
- Check that the ROS publishers are actively sending current rather than stale data.

### The Confirm button remains disabled

- Check `ready_for_confirm` and the `confirm_blockers` array in the candidate packet.
- Confirm that a non-expired candidate is selected.
- Confirm that no command is already pending.
- Review the displayed positioning, altitude, flight-mode, and flight-control readiness information.

## Scope and Limitations

This repository is complete as the **Unity client project**: it includes `Assets`, `Packages`, and `ProjectSettings`. It does not include the ROS detection pipeline, rosbridge deployment, autopilot configuration, or the external flight-control bridge. Those services must be configured separately for the end-to-end system to operate.

## License

No project-wide open-source license has been assigned. Third-party packages, plug-ins, models, and sample assets remain subject to their respective licenses.
