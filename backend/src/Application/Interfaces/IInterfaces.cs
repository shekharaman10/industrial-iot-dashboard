// ─── IInterfaces.cs ───────────────────────────────────────────────────────────
// This file previously contained all four application interfaces and AnalysisResult.
// Each has been moved to its own dedicated file for clarity and single responsibility:
//
//   ISensorRepository.cs  — time-series read/write (InfluxDB)
//   IAlertRepository.cs   — alert CRUD + ACK (PostgreSQL)
//   IAnalyticsEngine.cs   — stateful Z-score engine + AnalysisResult record
//   IDeviceRepository.cs  — device registry (PostgreSQL)
//   IMessageBus.cs        — channel abstraction (swap-to-Kafka path)
//
// This file is intentionally empty and kept only for source-control history.
// Do not add new types here.

namespace IotDashboard.Application.Interfaces;
