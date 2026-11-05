import { api, downloadFile } from "./client";
import type {
  AlertDto,
  AlertRuleDto,
  DeviceCommandDto,
  DeviceConfigurationDto,
  DeviceLifecycleSettingsDto,
  DeviceDto,
  DeviceValueConversionDto,
  DigitalTwinSettingsDto,
  DeviceTypeDto,
  DeviceTypeParameterDto,
  AuditLogDto,
  FirmwareDeploymentDto,
  FirmwareDto,
  LatestTelemetryDto,
  LoginResponse,
  ManagedUserDto,
  MqttCredentialDto,
  PagedResult,
  GlobalAlertRuleDto,
  EffectiveAlertRuleDto,
  ParameterCalculationDto,
  DashboardWidget,
  RawMessageDto,
  SiteDto,
  SmtpSettingsDto,
  TelegramSettingsDto,
  TelemetryPointDto,
  TelemetryRange,
  TelemetryRollupPointDto,
  TenantDto,
  CalculationAssignmentMode,
  CalculationGranularity,
  DeviceCalculationsDto,
  EligibleCalculationRuleDto,
  CalculationResultsDto,
  CalculationRuleDeviceDto,
  CalculationRuleDto,
  CalculationTypeDto,
  ParameterKind,
  DeviceBaselineDto,
  ConnectorDto,
  ConnectorSettings,
  ConnectorTestResult,
  ConnectorCaptureDto,
  ConnectorUnknownDeviceDto,
  ConnectorUnmappedKeyDto,
  ConnectorServiceStatusDto,
  GatewayDto,
  HubDto,
} from "@/types/api";

export const AuthApi = {
  login: (email: string, password: string) => api.post<LoginResponse>("/api/auth/login", { email, password }),
  logout: (refreshToken: string | null) => api.post<void>("/api/auth/logout", { refreshToken }),
  /** Signs every other session out; this one continues with the tokens returned. */
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post<LoginResponse>("/api/auth/change-password", { currentPassword, newPassword }),
};

export const TenantsApi = {
  list: (page = 1, pageSize = 50) => api.get<PagedResult<TenantDto>>(`/api/tenants?page=${page}&pageSize=${pageSize}`),
  get: (id: string) => api.get<TenantDto>(`/api/tenants/${id}`),
  create: (body: { name: string; slug: string }) => api.post<TenantDto>("/api/tenants", body),
  update: (id: string, body: { name: string; isActive: boolean }) => api.put<TenantDto>(`/api/tenants/${id}`, body),
};

export const UsersApi = {
  // tenantId omitted lists across every tenant — only meaningful for a SuperAdmin caller.
  list: (tenantId?: string) => api.get<ManagedUserDto[]>(`/api/users${toQuery({ tenantId })}`),
  create: (body: { email: string; displayName: string; password: string; role: string; tenantId?: string; telegramChatId?: string }) =>
    api.post<ManagedUserDto>("/api/users", body),
  update: (id: string, body: { displayName: string; telegramChatId?: string; role?: string; password?: string }) =>
    api.put<ManagedUserDto>(`/api/users/${id}`, body),
  deactivate: (id: string) => api.post<void>(`/api/users/${id}/deactivate`),
  activate: (id: string) => api.post<void>(`/api/users/${id}/activate`),
};

export const SitesApi = {
  // tenantId omitted lists across every tenant — only meaningful for a SuperAdmin caller.
  list: (tenantId?: string) => api.get<PagedResult<SiteDto>>(`/api/sites${toQuery({ tenantId, pageSize: 100 })}`),
  get: (id: string, tenantId: string) => api.get<SiteDto>(`/api/sites/${id}?tenantId=${tenantId}`),
  create: (tenantId: string, body: { code: string; name: string; address?: string; latitude?: number; longitude?: number }) =>
    api.post<SiteDto>(`/api/sites?tenantId=${tenantId}`, body),
  update: (id: string, tenantId: string, body: { name: string; address?: string; latitude?: number; longitude?: number }) =>
    api.put<SiteDto>(`/api/sites/${id}?tenantId=${tenantId}`, body),
};

type DeviceTypeBody = { name: string; description?: string; expectedIntervalSeconds?: number | null; supportsCommands?: boolean };

export const DeviceTypesApi = {
  list: () => api.get<DeviceTypeDto[]>("/api/device-types"),
  create: (body: DeviceTypeBody) => api.post<DeviceTypeDto>("/api/device-types", body),
  update: (id: string, body: DeviceTypeBody) => api.put<DeviceTypeDto>(`/api/device-types/${id}`, body),
  // Refused with 409 DEVICE_TYPE_IN_USE while any device, firmware or rule still uses the type.
  remove: (id: string) => api.delete<void>(`/api/device-types/${id}`),
  addParameter: (
    deviceTypeId: string,
    body: { key: string; label: string; unit?: string; minValue?: number; maxValue?: number; kind?: ParameterKind; calculation?: ParameterCalculationDto; dashboardWidget?: DashboardWidget | null },
  ) =>
    api.post<DeviceTypeParameterDto>(`/api/device-types/${deviceTypeId}/parameters`, body),
  updateParameter: (
    deviceTypeId: string,
    parameterId: string,
    body: { label: string; unit?: string; minValue?: number; maxValue?: number; kind?: ParameterKind; calculation?: ParameterCalculationDto; dashboardWidget?: DashboardWidget | null },
  ) =>
    api.put<DeviceTypeParameterDto>(`/api/device-types/${deviceTypeId}/parameters/${parameterId}`, body),
  removeParameter: (deviceTypeId: string, parameterId: string) =>
    api.delete<void>(`/api/device-types/${deviceTypeId}/parameters/${parameterId}`),
};

export interface DeviceListParams {
  tenantId?: string;
  siteId?: string;
  deviceTypeId?: string;
  transport?: string;
  protocol?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

function toQuery(params: object): string {
  const parts = Object.entries(params)
    .filter(([, v]) => v !== undefined && v !== null && v !== "")
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);
  return parts.length ? `?${parts.join("&")}` : "";
}

export const ConnectorsApi = {
  list: () => api.get<ConnectorDto[]>("/api/connectors"),
  get: (id: string) => api.get<ConnectorDto>(`/api/connectors/${id}`),
  create: (body: ConnectorSettings) => api.post<ConnectorDto>("/api/connectors", body),
  update: (id: string, body: ConnectorSettings) => api.put<ConnectorDto>(`/api/connectors/${id}`, body),
  remove: (id: string) => api.delete<void>(`/api/connectors/${id}`),
  /** Decodes a sample with the connector as drafted; nothing is saved. */
  test: (connector: ConnectorSettings, sample: string, sampleIsHex: boolean) =>
    api.post<ConnectorTestResult>("/api/connectors/test", { connector, sample, sampleIsHex }),
  capture: (id: string) => api.post<ConnectorCaptureDto>(`/api/connectors/${id}/capture`),
  getCapture: (captureId: string) => api.get<ConnectorCaptureDto>(`/api/connectors/captures/${captureId}`),
  unknownDevices: () => api.get<ConnectorUnknownDeviceDto[]>("/api/connectors/unknown-devices"),
  dismissUnknown: (id: string, sensorId: string) => api.delete<void>(`/api/connectors/${id}/unknown-devices/${encodeURIComponent(sensorId)}`),
  unmappedKeys: (id: string) => api.get<ConnectorUnmappedKeyDto[]>(`/api/connectors/${id}/unmapped-keys`),
  serviceStatus: () => api.get<ConnectorServiceStatusDto>("/api/connectors/service-status"),
};

export const GatewaysApi = {
  list: (params: { search?: string; status?: string; tenantId?: string; page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<GatewayDto>>(`/api/gateways${toQuery(params)}`),
  available: (tenantId?: string, siteId?: string) => api.get<GatewayDto[]>(`/api/gateways/available${toQuery({ tenantId, siteId })}`),
  get: (id: string) => api.get<GatewayDto>(`/api/gateways/${id}`),
  create: (body: { gatewayCode: string; name?: string; ipAddress?: string; firmwareVersion?: string }) => api.post<GatewayDto>("/api/gateways", body),
  update: (id: string, body: { name: string; ipAddress?: string; firmwareVersion?: string }) => api.put<GatewayDto>(`/api/gateways/${id}`, body),
  remove: (id: string) => api.delete<void>(`/api/gateways/${id}`),
  command: (id: string, op: string, payload: Record<string, unknown> = {}) =>
    api.post<{ topic: string; operation: string }>(`/api/gateways/${id}/commands`, { op, payload }),
};


export const HubsApi = {
  list: (params: { search?: string; status?: string; tenantId?: string; siteId?: string; page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<HubDto>>(`/api/hubs${toQuery(params)}`),
  get: (id: string) => api.get<HubDto>(`/api/hubs/${id}`),
  create: (body: { hubId: string; name?: string; siteId?: string; ipAddress?: string; appVersion?: string; agentVersion?: string; securityMode?: string }) =>
    api.post<HubDto>("/api/hubs", body),
  update: (id: string, body: { name: string; siteId?: string; ipAddress?: string; appVersion?: string; agentVersion?: string; securityMode?: string }) =>
    api.put<HubDto>(`/api/hubs/${id}`, body),
  remove: (id: string) => api.delete<void>(`/api/hubs/${id}`),
  command: (id: string, action: string, args: Record<string, unknown> = {}) =>
    api.post<{ topic: string; action: string; commandId: string }>(`/api/hubs/${id}/commands`, { action, arguments: args }),
  setDesiredConfig: (id: string, patch: { displayMode?: string; rotationSeconds?: number; welcomeSeconds?: number; historyRange?: string }) =>
    api.post<{ topic: string; requestId: string; desiredRevision: number; patch: Record<string, unknown> }>(`/api/hubs/${id}/config/desired`, patch),
};

export const DeviceBaselinesApi = {
  list: (deviceId: string) => api.get<DeviceBaselineDto[]>(`/api/devices/${deviceId}/baseline`),
  /** Takes the baseline from the device's latest reading; no keys = every measured parameter in it. */
  set: (deviceId: string, parameterKeys?: string[]) => api.post<DeviceBaselineDto[]>(`/api/devices/${deviceId}/baseline`, { parameterKeys }),
  reset: (deviceId: string, parameterKey?: string) =>
    api.delete<void>(`/api/devices/${deviceId}/baseline${parameterKey ? `?parameterKey=${encodeURIComponent(parameterKey)}` : ""}`),
};

export const DevicesApi = {
  list: (params: DeviceListParams) => api.get<PagedResult<DeviceDto>>(`/api/devices${toQuery(params)}`),
  get: (id: string) => api.get<DeviceDto>(`/api/devices/${id}`),
  lifecycleSettings: () => api.get<DeviceLifecycleSettingsDto>("/api/devices/lifecycle-settings"),
  create: (tenantId: string, body: Partial<DeviceDto> & Record<string, unknown>) => api.post<DeviceDto>(`/api/devices?tenantId=${tenantId}`, body),
  update: (id: string, body: Record<string, unknown>) => api.put<DeviceDto>(`/api/devices/${id}`, body),
  remove: (id: string) => api.delete<void>(`/api/devices/${id}`),
  /** All-null resets the twin to the device type's default template. */
  setDigitalTwin: (id: string, body: DigitalTwinSettingsDto) => api.put<DeviceDto>(`/api/devices/${id}/digital-twin`, body),
  /** Turns a device's alerts off (e.g. maintenance) or back on. */
  setAlerts: (id: string, enabled: boolean) => api.put<DeviceDto>(`/api/devices/${id}/alerts`, { enabled }),
  /** Replaces the device's unit corrections; an empty list removes them. MQTT-direct devices only. */
  setValueConversions: (id: string, conversions: DeviceValueConversionDto[]) =>
    api.put<DeviceDto>(`/api/devices/${id}/value-conversions`, { conversions }),
};

export const TelemetryApi = {
  latest: (tenantId?: string, siteId?: string) => api.get<LatestTelemetryDto[]>(`/api/telemetry/latest${toQuery({ tenantId, siteId })}`),
  latestForDevice: (deviceId: string) => api.get<LatestTelemetryDto>(`/api/devices/${deviceId}/telemetry`),
  history: (deviceId: string, range: TelemetryRange = "OneHour") =>
    api.get<TelemetryPointDto[]>(`/api/devices/${deviceId}/telemetry/history?range=${range}`),
  rollup: (deviceId: string, granularity: "Daily" | "Weekly" = "Daily") =>
    api.get<TelemetryRollupPointDto[]>(`/api/devices/${deviceId}/telemetry/rollup?granularity=${granularity}`),
};

export const AlertsApi = {
  list: (params: { tenantId?: string; deviceId?: string; status?: string; severity?: string; page?: number; pageSize?: number }) =>
    api.get<PagedResult<AlertDto>>(`/api/alerts${toQuery(params)}`),
  acknowledge: (id: string) => api.post<AlertDto>(`/api/alerts/${id}/acknowledge`),
  resolve: (id: string) => api.post<AlertDto>(`/api/alerts/${id}/resolve`),
  listRules: (tenantId: string) => api.get<AlertRuleDto[]>(`/api/alerts/rules?tenantId=${tenantId}`),
  /** The rules each device is evaluated against (standard rules with company/device changes applied), one entry per device. */
  effectiveRules: (tenantId: string) => api.get<AlertRuleDto[]>(`/api/alerts/rules/effective?tenantId=${tenantId}`),
  createRule: (
    tenantId: string,
    body: {
      name: string;
      metric: string;
      operator: string;
      threshold?: number;
      severity: string;
      cooldownSeconds: number;
      siteId?: string;
      deviceId?: string;
      deviceTypeId?: string;
    },
  ) => api.post<AlertRuleDto>(`/api/alerts/rules?tenantId=${tenantId}`, body),
  updateRule: (
    id: string,
    tenantId: string,
    body: { name: string; metric: string; operator: string; threshold?: number; severity: string; cooldownSeconds: number; isEnabled: boolean },
  ) => api.put<AlertRuleDto>(`/api/alerts/rules/${id}?tenantId=${tenantId}`, body),
  deleteRule: (id: string, tenantId: string) => api.delete<void>(`/api/alerts/rules/${id}?tenantId=${tenantId}`),
};

export const CommandsApi = {
  list: (deviceId: string) => api.get<DeviceCommandDto[]>(`/api/devices/${deviceId}/commands`),
  create: (deviceId: string, type: string, payloadJson?: string) =>
    api.post<DeviceCommandDto>(`/api/devices/${deviceId}/commands`, { type, payloadJson }),
};

export const ConfigurationApi = {
  get: (deviceId: string) => api.get<DeviceConfigurationDto>(`/api/devices/${deviceId}/configuration`),
  update: (deviceId: string, body: { reportingIntervalSeconds: number; heartbeatIntervalSeconds: number; temperatureThreshold: number | null }) =>
    api.put<DeviceConfigurationDto>(`/api/devices/${deviceId}/configuration`, body),
};

export const RawMessagesApi = {
  list: (deviceId: string, page = 1, pageSize = 25) => api.get<PagedResult<RawMessageDto>>(`/api/devices/${deviceId}/raw-messages?page=${page}&pageSize=${pageSize}`),
  replay: (id: string) => api.post<RawMessageDto>(`/api/raw-messages/${id}/replay`),
};

export const FirmwareApi = {
  list: () => api.get<FirmwareDto[]>("/api/firmware"),
  /** Uploads the binary with the release details; the server computes the real SHA-256 and size. */
  create: (body: { deviceTypeId: string; version: string; releaseNotes?: string; file: File }) => {
    const form = new FormData();
    form.append("deviceTypeId", body.deviceTypeId);
    form.append("version", body.version);
    if (body.releaseNotes) form.append("releaseNotes", body.releaseNotes);
    form.append("file", body.file);
    return api.upload<FirmwareDto>("/api/firmware", form);
  },
  remove: (id: string) => api.delete<void>(`/api/firmware/${id}`),
  downloadFile: (firmware: FirmwareDto) => downloadFile(`/api/firmware/${firmware.id}/file`, firmware.fileName),
  deploy: (firmwareId: string, deviceIds: string[]) => api.post<FirmwareDeploymentDto[]>("/api/firmware/deploy", { firmwareId, deviceIds }),
  deployments: (firmwareId: string) => api.get<FirmwareDeploymentDto[]>(`/api/firmware/deployments?firmwareId=${firmwareId}`),
  retry: (deploymentId: string) => api.post<FirmwareDeploymentDto>(`/api/firmware/deployments/${deploymentId}/retry`),
};

export interface SaveGlobalAlertRuleBody {
  name: string;
  deviceTypeId?: string | null;
  metric: string;
  operator: string;
  threshold?: number | null;
  severity: string;
  cooldownSeconds: number;
  isEnabled: boolean;
  appliesByDefault: boolean;
}

/** A change at one level — company (no site/device), a site, or a device. Null fields inherit; all null resets. */
export interface SaveAlertRuleOverrideBody {
  siteId?: string | null;
  deviceId?: string | null;
  isEnabled?: boolean | null;
  threshold?: number | null;
  severity?: string | null;
  cooldownSeconds?: number | null;
}

export const GlobalAlertRulesApi = {
  list: () => api.get<GlobalAlertRuleDto[]>("/api/alert-rules/global"),
  create: (body: SaveGlobalAlertRuleBody) => api.post<GlobalAlertRuleDto>("/api/alert-rules/global", body),
  update: (id: string, body: SaveGlobalAlertRuleBody) => api.put<GlobalAlertRuleDto>(`/api/alert-rules/global/${id}`, body),
  remove: (id: string) => api.delete<void>(`/api/alert-rules/global/${id}`),
  effective: (params: { tenantId: string; siteId?: string; deviceTypeId?: string; deviceId?: string }) =>
    api.get<EffectiveAlertRuleDto[]>(`/api/alert-rules/global/effective${toQuery(params)}`),
  saveOverride: (id: string, tenantId: string, body: SaveAlertRuleOverrideBody) =>
    api.put<void>(`/api/alert-rules/global/${id}/override?tenantId=${tenantId}`, body),
  resetOverride: (id: string, tenantId: string, level: { siteId?: string; deviceId?: string }) =>
    api.delete<void>(`/api/alert-rules/global/${id}/override${toQuery({ tenantId, ...level })}`),
};

export const MqttCredentialsApi = {
  list: () => api.get<MqttCredentialDto[]>("/api/mqtt-credentials"),
  create: (tenantId: string) => api.post<MqttCredentialDto>(`/api/mqtt-credentials?tenantId=${tenantId}`),
  rotate: (id: string) => api.post<MqttCredentialDto>(`/api/mqtt-credentials/${id}/rotate`),
  setPassword: (id: string, password: string) => api.put<MqttCredentialDto>(`/api/mqtt-credentials/${id}/password`, { password }),
  remove: (id: string) => api.delete<void>(`/api/mqtt-credentials/${id}`),
};

export const AuditLogApi = {
  list: (params: { tenantId?: string; page?: number; pageSize?: number; search?: string }) =>
    api.get<PagedResult<AuditLogDto>>(`/api/audit-log${toQuery(params)}`),
};

export const SmtpSettingsApi = {
  get: () => api.get<SmtpSettingsDto>("/api/settings/smtp"),
  update: (body: { host: string; port: number; username: string; password?: string; enableSsl: boolean; fromAddress: string; fromName: string }) =>
    api.put<SmtpSettingsDto>("/api/settings/smtp", body),
  sendTest: (toEmail: string) => api.post<void>("/api/settings/smtp/test", { toEmail }),
};

export const TelegramSettingsApi = {
  get: () => api.get<TelegramSettingsDto>("/api/settings/telegram"),
  update: (botToken?: string) => api.put<TelegramSettingsDto>("/api/settings/telegram", { botToken }),
  sendTest: (chatId: string) => api.post<void>("/api/settings/telegram/test", { chatId }),
};

export interface CalculationRuleBody {
  name: string;
  /** The one parameter a rule reads — every type but FORMULA. */
  sourceParameterKey?: string;
  /** What a FORMULA rule calculates, e.g. "voltage * current * powerFactor / 1000". */
  expression?: string;
  outputKey: string;
  outputName: string;
  outputUnit?: string;
  maxDelta?: number;
  /** Averaging period of a windowed rule (TWA, Leq); the server's default when omitted. */
  windowSeconds?: number;
  siteId?: string;
  deviceId?: string;
  deviceTypeId?: string;
  assignmentMode?: CalculationAssignmentMode;
}

export const CalculationsApi = {
  types: () => api.get<CalculationTypeDto[]>("/api/calculations/types"),
  listRules: (tenantId: string) => api.get<CalculationRuleDto[]>(`/api/calculations/rules?tenantId=${tenantId}`),
  createRule: (tenantId: string, body: CalculationRuleBody & { key: string; type: string }) =>
    api.post<CalculationRuleDto>(`/api/calculations/rules?tenantId=${tenantId}`, body),
  updateRule: (id: string, tenantId: string, body: CalculationRuleBody & { isEnabled: boolean }) =>
    api.put<CalculationRuleDto>(`/api/calculations/rules/${id}?tenantId=${tenantId}`, body),
  deleteRule: (id: string, tenantId: string) => api.delete<void>(`/api/calculations/rules/${id}?tenantId=${tenantId}`),
  /** Rules a device of this type/site can use — what the device form's Calculations dropdown offers. */
  eligible: (tenantId: string, deviceTypeId: string, siteId?: string, deviceId?: string) =>
    api.get<EligibleCalculationRuleDto[]>(`/api/calculations/eligible${toQuery({ tenantId, deviceTypeId, siteId, deviceId })}`),
  deviceCalculations: (deviceId: string, tenantId: string) =>
    api.get<DeviceCalculationsDto>(`/api/devices/${deviceId}/calculations?tenantId=${tenantId}`),
  setDeviceCalculations: (deviceId: string, tenantId: string, ruleIds: string[]) =>
    api.put<DeviceCalculationsDto>(`/api/devices/${deviceId}/calculations?tenantId=${tenantId}`, { ruleIds }),
  ruleDevices: (id: string, tenantId: string) =>
    api.get<CalculationRuleDeviceDto[]>(`/api/calculations/rules/${id}/devices?tenantId=${tenantId}`),
  results: (id: string, params: { tenantId: string; deviceId: string; from: string; to: string; granularity: CalculationGranularity }) =>
    api.get<CalculationResultsDto>(`/api/calculations/rules/${id}/results${toQuery(params)}`),
};
