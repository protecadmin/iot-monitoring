import type { TiltAxes } from "./tilt";
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  role: string;
  tenantId: string | null;
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  user: UserDto;
}

export interface ManagedUserDto {
  allowedSiteIds?: string[];
  id: string;
  email: string;
  displayName: string;
  role: string;
  tenantId: string | null;
  isActive: boolean;
  createdAt: string;
  telegramChatId: string | null;
}

export interface TenantDto {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  createdAt: string;
  digitalTwinEnabled: boolean;
}

export interface SiteDto {
  id: string;
  tenantId: string;
  code: string;
  name: string;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
}

/** What a parameter's value means over time — declared, never inferred from its unit. */
export type ParameterKind = "Instantaneous" | "Counter" | "Digital" | "Derived";

export interface DeviceTypeParameterDto {
  id: string;
  key: string;
  label: string;
  unit: string | null;
  minValue: number | null;
  maxValue: number | null;
  kind: ParameterKind;
  /** How a Derived parameter is calculated — for every tenant (each gets an automatic rule kept in step with it). */
  calculation?: ParameterCalculationDto | null;
  /** Only on a save: tenants whose rule couldn't be updated (e.g. another rule already outputs this key). */
  warnings?: string[] | null;
  /** How the dashboard shows it, for every company; null keeps it off the dashboard. */
  dashboardWidget?: DashboardWidget | null;
}

export type DashboardWidget = "Consumption" | "LiveValue" | "LevelGauge" | "SafetyStatus" | "OnOff";

export interface ParameterCalculationDto {
  /** API name: FORMULA, TOTALIZER_DELTA, TIME_WEIGHTED_AVERAGE or EQUIVALENT_CONTINUOUS_LEVEL. */
  type: string;
  expression?: string | null;
  sourceParameterKey?: string | null;
  maxDelta?: number | null;
  /** Averaging period for a windowed type; null takes the default. */
  windowSeconds?: number | null;
}

export type DeviceConnection = "MqttDirect" | "Connector";

export type ConnectorFraming = "NewLine" | "FixedLength" | "EndCharacter" | "OnePerConnection" | "StartMarker";
export type ConnectorFormat = "Csv" | "KeyValue" | "Json" | "Binary";
export type ConnectorTarget = "SensorId" | "Time" | "Parameter" | "Ignore";

export interface ConnectorMappingDto {
  /** Column number (CSV), key, JSON path, or start byte (binary). Exact, case included. */
  source: string;
  target: ConnectorTarget;
  parameterKey: string | null;
  byteLength?: number | null;
  signed?: boolean;
  littleEndian?: boolean;
  /** value = (raw - offset) * scale */
  offset?: number;
  scale?: number;
}

export interface ConnectorSettings {
  name: string;
  vendor: string | null;
  defaultDeviceTypeId: string | null;
  protocol: "Tcp";
  port: number;
  framing: ConnectorFraming;
  fixedLength: number | null;
  endCharacter: string | null;
  startMarker?: string | null;
  csvFieldCount?: number | null;
  maxMessageBytes: number;
  format: ConnectorFormat;
  csvSeparator: string;
  csvOptionalPrefix: boolean;
  pairSeparator: string;
  keyValueSeparator: string;
  binaryHeaderHex: string | null;
  binaryHeaderOffset: number;
  binaryChecksum: "None" | "Crc16Modbus";
  deviceCodePrefix: string;
  isEnabled: boolean;
  mappings: ConnectorMappingDto[];
}

export interface ConnectorDto extends ConnectorSettings {
  id: string;
  deviceCount: number;
  unmappedKeyCount: number;
  updatedAt: string | null;
}

export interface ConnectorTestResult {
  values: { source: string; value: string }[];
  sensorId: string | null;
  deviceCode: string | null;
  fields: Record<string, number>;
  time: string | null;
  unmappedSources: string[];
  error: string | null;
  validationErrors: string[];
}

export interface ConnectorCaptureDto {
  id: string;
  connectorId: string;
  requestedAt: string;
  expiresAt: string;
  capturedAt: string | null;
  sample: string | null;
  sampleIsHex: boolean;
}

export interface ConnectorUnknownDeviceDto {
  connectorId: string;
  connectorName: string;
  sensorId: string;
  deviceCode: string;
  firstSeenAt: string;
  lastSeenAt: string;
  messageCount: number;
  lastSample: string | null;
}

export interface ConnectorUnmappedKeyDto {
  connectorId: string;
  source: string;
  messageCount: number;
  lastSeenAt: string;
  lastValue: string | null;
}

export interface ConnectorServiceStatusDto {
  online: boolean;
  lastHeartbeatAt: string | null;
  version: string | null;
  messagesAccepted: number;
  messagesRejected: number;
  messagesUnknown: number;
  messagesPublished: number;
  lastError: string | null;
  connectorsJson: string | null;
  portRangeStart: number;
  portRangeEnd: number;
}

/** A device's reference reading for one parameter; "Change from baseline" rules measure from it. */
export interface DeviceBaselineDto {
  parameterKey: string;
  value: number;
  /** The time of the reading it was taken from. */
  readingAt: string;
  setAt: string;
}

export interface DeviceTypeDto {
  id: string;
  name: string;
  description: string | null;
  parameters: DeviceTypeParameterDto[];
  /** How often its devices report, when slower than the platform's offline window; null for the default. */
  expectedIntervalSeconds: number | null;
  /** False for devices with no downlink: no Commands tab, and the API refuses commands. */
  supportsCommands: boolean;
  tiltAxes?: TiltAxes | null;
}

export interface DeviceDto {
  mqttTelemetryTopic?: string | null;
  effectiveMqttTelemetryTopic?: string | null;
  id: string;
  tenantId: string;
  siteId: string;
  deviceTypeId: string;
  deviceCode: string;
  name: string;
  serialNumber: string | null;
  manufacturer: string | null;
  model: string | null;
  transport: string;
  protocol: string;
  payloadFormat: string;
  firmwareVersion: string | null;
  hardwareVersion: string | null;
  status: string;
  latitude: number | null;
  longitude: number | null;
  lastSeenAt: string | null;
  lastTelemetryAt: string | null;
  lastHeartbeatAt: string | null;
  gatewayId: string | null;
  gatewayName: string | null;
  createdAt: string;
  /** How the digital twin is drawn; null = defaults for the device type. */
  digitalTwin?: DigitalTwinSettingsDto | null;
  /** Off mutes the device's alerts (e.g. maintenance); readings are still stored. */
  alertsEnabled: boolean;
  /** "Received" when the device sends no time of its own: times are arrival times. */
  timeSource: "Device" | "Received";
  /** How its data arrives: BMS-format MQTT, or its own format through a connector. */
  connection: DeviceConnection;
  connectorId: string | null;
  /** The ID the device puts in its messages (connector devices). */
  sensorId: string | null;
  installedOn: string | null;
  mountingLocation: string | null;
  /** Unit corrections applied before storing — MQTT-direct devices only. */
  valueConversions?: DeviceValueConversionDto[] | null;
}

/** value = (raw − offset) × scale, applied to one parameter of an MQTT-direct device before it is stored. */
export interface DeviceValueConversionDto {
  parameterKey: string;
  offset: number;
  scale: number;
}

/** Template and mount point ids are defined in components/digital-twin/designs. Null = default. */
export interface DigitalTwinSettingsDto {
  template: string | null;
  mountPoint: string | null;
  assetName: string | null;
}

export interface TelemetryPointDto {
  timestamp: string;
  /** Measured values, exactly as the device reported them. */
  fields: Record<string, number>;
  /** Values calculated from them by the Calculations module — kept apart from `fields` so measured and calculated never blur. */
  derived?: Record<string, number> | null;
}

export interface LatestTelemetryDto {
  deviceId: string;
  point: TelemetryPointDto | null;
  calculated?: LatestCalculatedValueDto[] | null;
}

export interface AlertDto {
  id: string;
  tenantId: string;
  deviceId: string;
  alertRuleId: string;
  severity: string;
  status: string;
  parameter: string;
  currentValue: number | null;
  threshold: number | null;
  message: string;
  triggeredAt: string;
  acknowledgedAt: string | null;
  resolvedAt: string | null;
}

export interface DeviceCommandDto {
  id: string;
  deviceId: string;
  type: string;
  status: string;
  createdAt: string;
  sentAt: string | null;
  completedAt: string | null;
  error: string | null;
}

export interface DeviceConfigurationDto {
  deviceId: string;
  version: number;
  reportingIntervalSeconds: number;
  heartbeatIntervalSeconds: number;
  temperatureThreshold: number | null;
  acknowledgedAt: string | null;
}

export interface RawMessageDto {
  id: string;
  deviceId: string;
  transport: string;
  protocol: string;
  topic: string | null;
  endpoint: string | null;
  payloadFormat: string;
  rawPayloadBase64: string;
  receivedAt: string;
  deviceTimestamp: string | null;
  correlationId: string;
  processingStatus: string;
  processingError: string | null;
}

export interface FirmwareDto {
  id: string;
  deviceTypeId: string;
  version: string;
  fileName: string;
  sha256Checksum: string;
  sizeBytes: number;
  releaseNotes: string | null;
  releaseDate: string;
  /** False for releases created before files were stored — those can't be deployed. */
  hasFile: boolean;
}

export interface FirmwareDeploymentDto {
  id: string;
  firmwareId: string;
  deviceId: string;
  status: string;
  attemptCount: number;
  error: string | null;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
}

export type TelemetryRange = "Live" | "OneHour" | "SixHours" | "TwentyFourHours" | "SevenDays";

export interface AlertRuleDto {
  id: string;
  siteId: string | null;
  deviceId: string | null;
  deviceTypeId: string | null;
  name: string;
  metric: string;
  operator: string;
  threshold: number | null;
  severity: string;
  cooldownSeconds: number;
  isEnabled: boolean;
}

/** An alert rule defined once for every company, by device type. */
export interface GlobalAlertRuleDto {
  id: string;
  name: string;
  /** Null: every device type — the lifecycle conditions. */
  deviceTypeId: string | null;
  metric: string;
  operator: string;
  threshold: number | null;
  severity: string;
  cooldownSeconds: number;
  isEnabled: boolean;
  /** On: every company unless switched off. Off: only where switched on. */
  appliesByDefault: boolean;
  /** Companies, sites and devices that changed this rule. */
  overrideCount: number;
}

/** Where an effective value was set. */
export type AlertRuleLevel = "Global" | "Company" | "Site" | "Device";

export interface AlertRuleOverrideDto {
  siteId: string | null;
  deviceId: string | null;
  isEnabled: boolean | null;
  threshold: number | null;
  severity: string | null;
  cooldownSeconds: number | null;
}

/** A global rule as it applies at the selected company / site / device. */
export interface EffectiveAlertRuleDto {
  ruleId: string;
  name: string;
  deviceTypeId: string | null;
  metric: string;
  operator: string;
  isEnabled: boolean;
  enabledFrom: AlertRuleLevel;
  threshold: number | null;
  thresholdFrom: AlertRuleLevel;
  severity: string;
  severityFrom: AlertRuleLevel;
  cooldownSeconds: number;
  cooldownFrom: AlertRuleLevel;
  /** The change stored at exactly the selected level, if any. */
  here: AlertRuleOverrideDto | null;
}

export interface MqttCredentialDto {
  id: string;
  tenantId: string;
  tenantName: string;
  username: string;
  password: string;
  createdAt: string;
  rotatedAt: string | null;
}


export interface HubDto {
  id: string;
  tenantId: string | null;
  tenantName?: string | null;
  siteId: string | null;
  siteName: string;
  hubId: string;
  name: string;
  status: string;
  ipAddress: string | null;
  appVersion?: string | null;
  agentVersion?: string | null;
  securityMode?: string | null;
  rssi?: number | null;
  uptimeSeconds?: number | null;
  configRevision: number;
  configHash?: string | null;
  displayMode?: string | null;
  rotationSeconds?: number | null;
  welcomeSeconds?: number | null;
  historyRange?: string | null;
  lastSeenAt: string | null;
  lastHeartbeatAt?: string | null;
  lastMeasurementAt?: string | null;
  lastConfigSyncAt?: string | null;
  desiredConfigJson?: string | null;
  reportedConfigJson?: string | null;
  lastStatusJson?: string | null;
}

export interface GatewayDto {
  id: string;
  tenantId: string | null;
  siteId: string | null;
  siteName: string;
  tenantName?: string | null;
  name: string;
  gatewayCode: string;
  status: string;
  ipAddress: string | null;
  firmwareVersion?: string | null;
  rssi?: number | null;
  batteryPct?: number | null;
  lastSeenAt: string | null;
  lastMeasurementAt?: string | null;
  deviceNames?: string[];
}

export interface SmtpSettingsDto {
  host: string;
  port: number;
  username: string;
  hasPassword: boolean;
  enableSsl: boolean;
  fromAddress: string;
  fromName: string;
}

export interface TelegramSettingsDto {
  hasBotToken: boolean;
}

export interface AuditLogDto {
  id: string;
  tenantId: string | null;
  tenantName: string | null;
  userId: string | null;
  userEmail: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  detailsJson: string | null;
  createdAt: string;
}

export interface TelemetryFieldStatsDto {
  min: number;
  mean: number;
  max: number;
}

/** A calculated value over one period: a level (formula) has min/mean/max, a quantity (totalizer delta) a total. */
export interface CalculatedStatsDto {
  min: number | null;
  mean: number | null;
  max: number | null;
  total: number | null;
}

export interface TelemetryRollupPointDto {
  periodStart: string;
  fields: Record<string, TelemetryFieldStatsDto>;
  derived?: Record<string, CalculatedStatsDto> | null;
}

// ---- Calculations ----

/** Automatic: every device in the rule's scope, including new ones. Selected: only devices it's ticked on in the device form. */
export type CalculationAssignmentMode = "Automatic" | "Selected";

export interface EligibleCalculationRuleDto {
  rule: CalculationRuleDto;
  appliesAutomatically: boolean;
}

export interface DeviceCalculationsDto {
  deviceId: string;
  ruleIds: string[];
}

export type CalculationGranularity = "Reading" | "FiveMinutes" | "Hourly" | "Daily" | "Monthly";

export interface CalculationTypeDto {
  type: string;
  name: string;
  description: string;
  sourceRequirement: string;
}

export interface CalculationRuleDto {
  id: string;
  tenantId: string;
  siteId: string | null;
  deviceId: string | null;
  deviceTypeId: string | null;
  name: string;
  key: string;
  type: string;
  sourceParameterKey: string;
  outputKey: string;
  outputName: string;
  outputUnit: string | null;
  maxDelta: number | null;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  assignmentMode: CalculationAssignmentMode;
  /** A FORMULA rule's arithmetic over parameters, e.g. "voltage * current * powerFactor / 1000"; null for other types. */
  expression: string | null;
  /** Averaging period in seconds for a windowed type; null for every other type. */
  windowSeconds: number | null;
  /** A tenant's copy of a Derived parameter's calculation — edited on Device Types, only enabled/disabled here. */
  definedOnDeviceType: boolean;
}

export interface CalculatedPointDto {
  timestamp: string;
  value: number;
}

export interface LatestCalculatedValueDto {
  ruleId: string;
  outputKey: string;
  outputName: string;
  outputUnit: string | null;
  value: number;
  timestamp: string;
}

export interface CalculationResultsDto {
  ruleId: string;
  deviceId: string;
  outputKey: string;
  outputName: string;
  outputUnit: string | null;
  granularity: CalculationGranularity;
  from: string;
  to: string;
  points: CalculatedPointDto[];
  total: number;
  latest: LatestCalculatedValueDto | null;
}

export interface CalculationRuleDeviceDto {
  deviceId: string;
  deviceName: string;
  deviceCode: string;
  siteId: string;
  latest: LatestCalculatedValueDto | null;
}

/** Payload of the CalculationUpdated realtime event. */
export interface CalculationUpdatedEvent {
  deviceId: string;
  siteId: string;
  ruleId: string;
  ruleKey: string;
  outputKey: string;
  outputName: string;
  outputUnit: string | null;
  value: number;
  timestamp: string;
}

/** When devices go Online/Offline — the backend's DeviceLifecycle settings. */
export interface DeviceLifecycleSettingsDto {
  offlineAfterSeconds: number;
  sweepIntervalSeconds: number;
  noTelemetryAlertAfterSeconds: number;
  deviceOfflineAlertAfterSeconds: number;
}
