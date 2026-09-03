# Unity UAV Target Confirmation & Autonomous Flight

以 Unity 建置的無人機目標確認介面。系統透過 ROS bridge 接收即時影像、候選目標與 UAV 遙測資料，讓操作員在 Unity 儀表板中檢視候選物、確認目標，並送出飛行命令，使 UAV 前往已確認目標上方 **20 公尺**。

> 本儲存庫目前保存 Unity 的 `Assets/`。專案開發版本為 **Unity 2022.3.51f1 (LTS)**。

## 主要功能

- 接收並顯示 UAV 相機的壓縮影像
- 顯示目標類別、追蹤 ID、GPS 位置、定位誤差與預覽圖
- 支援多個候選目標的上一筆／下一筆切換
- 依 ROS 回傳狀態控制「確認」按鈕，避免在條件未完成時送出命令
- 發布目標確認與取消命令
- 接收命令狀態：接受、執行、抵達、拒絕或失敗
- 以 Cesium 經緯度座標更新 3D 無人機位置
- 支援 GPS、相對高度、飛行模式、Arm 狀態及 IMU 姿態顯示
- 內建 Mock 測試快捷鍵，可在沒有 ROS 環境時驗證 UI 流程

## 系統流程

```text
相機 / 目標偵測 / UAV 飛控
            │
            ▼
         ROS bridge
            │
   ┌────────┼───────────────┐
   ▼        ▼               ▼
即時影像   候選目標 JSON    UAV 遙測 / IMU
   │        │               │
   └────────┴──────┬────────┘
                   ▼
          Unity 操作員儀表板
                   │
        選擇目標 → 人工確認 / 取消
                   │
                   ▼
          ROS 目標命令與狀態回報
                   │
                   ▼
       UAV 前往確認目標上方 20 m
```

## 核心 ROS Topics

| Topic | 類型 | 方向 | 用途 |
|---|---|---|---|
| `/d455i/color/image_annotated/compressed` | `sensor_msgs/CompressedImage` | ROS → Unity | 即時標註影像 |
| `/unity/detections_json` | `std_msgs/String` (JSON) | ROS → Unity | 候選目標、座標、信心值與確認條件 |
| `/uav/telemetry_json` | `std_msgs/String` (JSON) | ROS → Unity | GPS、高度、飛行模式、Arm 與定位狀態 |
| `/uav/imu` | `sensor_msgs/Imu` | ROS → Unity | UAV 姿態（選用） |
| `/unity/target_command` | `std_msgs/String` (JSON) | Unity → ROS | 確認目標或取消任務 |
| `/unity/target_command_status` | `std_msgs/String` (JSON) | ROS → Unity | 命令接受、執行、抵達、拒絕與失敗狀態 |

Topics 可在各元件的 Inspector 中調整；上表為程式預設值。

## 主要腳本

| 路徑 | 說明 |
|---|---|
| `Assets/Script/UAVDashboard/CompressedImageUIReceiver.cs` | 接收壓縮影像並更新 Unity `RawImage` |
| `Assets/Script/UAVDashboard/TargetCandidateSubscriber.cs` | 訂閱並解析候選目標 JSON |
| `Assets/Script/UAVDashboard/TargetSelectionUIController.cs` | 管理候選目標、確認視窗、按鈕狀態與操作流程 |
| `Assets/Script/UAVDashboard/TargetCommandPublisher.cs` | 發布確認／取消命令並追蹤命令結果；最終高度固定為 20 m |
| `Assets/Script/UAVDashboard/UavRosBridgeStateReceiver.cs` | 接收 GPS、遙測與 IMU，更新 Cesium 無人機模型 |
| `Assets/Script/UAVDashboard/TargetCandidateMockTester.cs` | 不連接 ROS 時的本機 UI 與狀態測試 |

## 使用方式

### 1. 建立或準備 Unity 專案

使用 Unity Hub 建立 **Unity 2022.3.51f1** 專案，關閉 Unity 後，將本儲存庫的 `Assets/` 放入專案根目錄。

### 2. 安裝必要套件

此專案資源使用下列主要套件：

- Cesium for Unity `1.23.3`
- ROS#（Siemens ROS Sharp）
- Unity Robotics ROS-TCP-Connector
- TextMeshPro `3.0.7`
- XR Interaction Toolkit `2.6.3`
- OpenXR `1.13.2`
- XR Hands `1.4.3`
- Meta XR SDK `69.0.1`
- Animation Rigging `1.2.1`

若使用既有完整專案，請保留原本的 `Packages/manifest.json` 與 `ProjectSettings/`，Unity 會依設定還原套件。

### 3. 設定 ROS 連線

1. 在場景中找到 `RosConnector`。
2. 將 WebSocket URL 設為 ROS bridge 主機，例如 `ws://<ROS_IP>:9090`。
3. 確認偵測、遙測、影像與命令 Topics 與 ROS 端一致。
4. 啟動 rosbridge、目標偵測節點與 UAV bridge。
5. 在 Unity 按下 Play，確認影像、GPS 與候選目標開始更新。

### 4. 確認目標

1. 使用上一筆／下一筆按鈕檢視候選目標。
2. 確認追蹤 ID、座標、信心值、位置誤差與預覽影像。
3. 系統顯示 `ready_for_confirm` 且無阻擋條件時，按下 **Confirm**。
4. Unity 發布命令後，介面會顯示 UAV 的接受、執行與抵達狀態。
5. 最終任務高度由程式固定為目標上方 **20 m**。

## 無 ROS 測試

場景若掛載 `TargetCandidateMockTester`，可使用以下快捷鍵測試完整 UI 狀態：

| 按鍵 | 動作 |
|---|---|
| `F6` | 注入候選目標 |
| `F7` | 模擬命令已接受 |
| `F8` | 模擬命令執行中 |
| `F9` | 模擬已抵達目標 |
| `F10` | 模擬命令被拒絕 |
| `F11` | 清除測試資料 |

## 目錄重點

```text
Assets/
├── CesiumSettings/       # Cesium 設定
├── drone_model/          # UAV 3D 模型
├── model/                # 機器人與感測器模型
├── Plugins/              # ROS#、OpenCV 與其他外掛
├── Resources/            # 執行期載入資源
├── Scenes/               # Unity 場景
├── Script/
│   ├── UAVDashboard/     # 目標確認與自動飛行介面
│   ├── PointCloudStreaming/
│   ├── RTT_scripts/
│   ├── HandTracking_control/
│   └── stretch_control/
└── StreamingAssets/      # 執行期串流資源
```

## 安全注意事項

本專案會發布可能影響實體 UAV 的飛行命令。實機測試前請先完成模擬器與 Mock 測試，確認 GPS、座標系、返航／失聯策略、地理圍欄、飛行模式及緊急停止機制皆正確。操作員應全程保有接管能力，並遵守當地無人機法規與場域安全規範。

## License

目前尚未指定開源授權。除非另有書面許可，請勿將本專案內容用於未授權的散布或商業用途；第三方套件與模型仍各自受其原始授權條款約束。
