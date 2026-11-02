"use client";

import { useEffect, useState } from "react";
import { TiltApi } from "@/lib/api/tilt";
import { TiltScheduleFields } from "@/components/devices/TiltScheduleFields";
import { useApi } from "@/hooks/useApi";
import { CalculationsApi, ConnectorsApi, DeviceTypesApi, DevicesApi, SitesApi, TenantsApi } from "@/lib/api/resources";
import { useCan } from "@/lib/auth/permissions";
import { ApiError } from "@/lib/api/client";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { DeviceCalculationsPicker } from "@/components/calculations/DeviceCalculationsPicker";
import { ValueConversionsEditor, parseRows, toRows, type ConversionRow } from "@/components/devices/ValueConversionsEditor";
import type { DeviceConnection, DeviceDto } from "@/types/api";


export function DeviceFormModal({
  device,
  fixedTenantId,
  prefill,
  onClose,
  onSaved,
}: {
  device?: DeviceDto;
  fixedTenantId?: string;
  /** Starts a new device on a connector — e.g. "Add device" on an unknown sensor ID. */
  prefill?: { connectorId: string; sensorId: string };
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = !!device;
  const can = useCan();
  const connectorsQuery = useApi(() => (can("connectors.view") ? ConnectorsApi.list() : Promise.resolve([])), []);

  const tenantsQuery = useApi(() => (fixedTenantId || isEdit ? Promise.resolve(undefined) : TenantsApi.list()), [fixedTenantId, isEdit]);
  const deviceTypesQuery = useApi(() => DeviceTypesApi.list(), []);

  const [tenantId, setTenantId] = useState(fixedTenantId ?? device?.tenantId ?? "");
  const sitesQuery = useApi(() => (tenantId ? SitesApi.list(tenantId) : Promise.resolve(undefined)), [tenantId]);

  const [siteId, setSiteId] = useState(device?.siteId ?? "");
  // For a new device that's whatever site was just picked above; for editing,
  // the device's site never changes in this form, so use its existing one.
  const effectiveSiteId = isEdit ? device!.siteId : siteId;
  const [deviceTypeId, setDeviceTypeId] = useState(device?.deviceTypeId ?? "");
  const [deviceCode, setDeviceCode] = useState(device?.deviceCode ?? "");
  // How its data arrives. Fixed after creation: the device code (and so its MQTT topic) depends on it.
  const [connection, setConnection] = useState<DeviceConnection>(device?.connection ?? (prefill ? "Connector" : "MqttDirect"));
  const [connectorId, setConnectorId] = useState(device?.connectorId ?? prefill?.connectorId ?? "");
  const [sensorId, setSensorId] = useState(device?.sensorId ?? prefill?.sensorId ?? "");
  const [installedOn, setInstalledOn] = useState(device?.installedOn ?? "");
  const [mountingLocation, setMountingLocation] = useState(device?.mountingLocation ?? "");
  const [name, setName] = useState(device?.name ?? "");
  const [serialNumber, setSerialNumber] = useState(device?.serialNumber ?? "");
  const [manufacturer, setManufacturer] = useState(device?.manufacturer ?? "");
  const [model, setModel] = useState(device?.model ?? "");
  const [latitude, setLatitude] = useState(device?.latitude?.toString() ?? "");
  const [longitude, setLongitude] = useState(device?.longitude?.toString() ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // Selected-mode calculation rules for this device, chosen right here rather
  // than mapped afterwards. For an existing device, start from what it has.
  const [calculationRuleIds, setCalculationRuleIds] = useState<string[]>([]);
  const assignmentsQuery = useApi(
    () => (isEdit ? CalculationsApi.deviceCalculations(device!.id, device!.tenantId) : Promise.resolve(undefined)),
    [isEdit, device?.id],
  );
  useEffect(() => {
    if (assignmentsQuery.data) setCalculationRuleIds(assignmentsQuery.data.ruleIds);
  }, [assignmentsQuery.data]);

  const connectors = (connectorsQuery.data ?? []).filter((c) => c.isEnabled || c.id === connectorId);
  const connector = connectors.find((c) => c.id === connectorId);
  const chosenType = deviceTypesQuery.data?.find((t) => t.id === deviceTypeId);
  const [tiltDraft, setTiltDraft] = useState<{ closingTime: string; timeZoneId: string } | null>(null);
  const tiltQuery = useApi(
    () => isEdit && chosenType?.tiltAxes ? TiltApi.get(device!.id) : Promise.resolve(undefined),
    [isEdit, device?.id, !!chosenType?.tiltAxes],
  );
  const tiltClosingTime = tiltDraft?.closingTime ?? tiltQuery.data?.settings?.closingTime ?? "23:59";
  const tiltTimeZone = tiltDraft?.timeZoneId ?? tiltQuery.data?.settings?.timeZoneId ?? "Asia/Singapore";
  const setTiltClosingTime = (closingTime: string) => setTiltDraft({ closingTime, timeZoneId: tiltTimeZone });
  const setTiltTimeZone = (timeZoneId: string) => setTiltDraft({ closingTime: tiltClosingTime, timeZoneId });
  // Does the connector fill this type's parameters? None at all means nothing would be stored.
  const mappedKeys = (connector?.mappings ?? []).filter((m) => m.target === "Parameter" && m.parameterKey).map((m) => m.parameterKey!.toLowerCase());
  const typeKeys = (chosenType?.parameters ?? []).filter((p) => p.kind !== "Derived").map((p) => p.key.toLowerCase());
  const matching = typeKeys.filter((k) => mappedKeys.includes(k)).length;
  const connectorMismatch = connection === "Connector" && !!connector && !!chosenType && mappedKeys.length > 0 && matching === 0;
  const codeFromSensor = connector && sensorId.trim() ? connector.deviceCodePrefix + sensorId.trim() : "";

  // Unit corrections — MQTT-direct only; a connector device converts in its connector's mapping.
  const [conversionRows, setConversionRows] = useState<ConversionRow[]>(() => toRows(device?.valueConversions));
  const isMqttDirect = (isEdit ? device.connection : connection) === "MqttDirect";
  const conversionType = isEdit ? deviceTypesQuery.data?.find((t) => t.id === device.deviceTypeId) : chosenType;

    async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!isEdit && !tenantId) {
      setError("Select a tenant");
      return;
    }
    if (!isEdit && !siteId) {
      setError("Select a site");
      return;
    }
    if (!isEdit && connection === "Connector") {
      if (!connector) return setError("Pick the connector this device sends to");
      if (!sensorId.trim()) return setError("Enter the sensor ID the device sends (on its label)");
      if (connectorMismatch) return setError(`${connector.name} sends none of ${chosenType?.name}'s parameters — pick another connector or device type.`);
    }

    if (chosenType?.tiltAxes && isEdit && (tiltQuery.loading || tiltQuery.error)) return setError(tiltQuery.error ?? "Wait for the daily tilt settings to load.");

    const parsedConversions = parseRows(isMqttDirect ? conversionRows : []);
    if (parsedConversions.error) return setError(parsedConversions.error);
    // Saved only when there is something to change, so an ordinary edit never touches them.
    const conversionsChanged = JSON.stringify(parsedConversions.conversions) !== JSON.stringify(isEdit ? (device.valueConversions ?? []) : []);

    setSaving(true);
    try {
      const latNum = latitude ? Number(latitude) : undefined;
      const lngNum = longitude ? Number(longitude) : undefined;

      let saved;
      if (isEdit) {
        saved = await DevicesApi.update(device.id, {
          name,
          serialNumber: serialNumber || undefined,
          manufacturer: manufacturer || undefined,
          model: model || undefined,
          latitude: latNum,
          longitude: lngNum,
          // Gateways aren't managed in the dashboard; keep whatever is stored.
          gatewayId: device.gatewayId ?? undefined,
          installedOn: installedOn || undefined,
          mountingLocation: mountingLocation || undefined,
        });
      } else {
        saved = await DevicesApi.create(tenantId, {
          siteId,
          deviceTypeId,
          deviceCode: connection === "Connector" ? codeFromSensor : deviceCode,
          connection,
          connectorId: connection === "Connector" ? connectorId : undefined,
          sensorId: connection === "Connector" ? sensorId.trim() : undefined,
          installedOn: installedOn || undefined,
          mountingLocation: mountingLocation || undefined,
          name,
          serialNumber: serialNumber || undefined,
          manufacturer: manufacturer || undefined,
          model: model || undefined,
          latitude: latNum,
          longitude: lngNum,
        });
      }

      if (chosenType?.tiltAxes) {
        try { await TiltApi.configure(saved.id, tiltClosingTime, tiltTimeZone.trim()); }
        catch (err) {
          onSaved();
          setError(`Device saved, but daily tilt settings were not saved: ${err instanceof ApiError ? err.message : "unexpected error"}. Open the device's Daily tilt tab to configure them.`);
          return;
        }
      }

      // Saved as a second call because the Calculations module owns the
      // device ↔ rule link. The device is already saved at this point, so a
      // failure here must not look like the whole form failed.
      try {
        await CalculationsApi.setDeviceCalculations(saved.id, isEdit ? device.tenantId : tenantId, calculationRuleIds);
      } catch (err) {
        onSaved();
        setError(
          `Device saved, but its calculations weren't: ${err instanceof ApiError ? err.message : "unexpected error"}. Edit the device to set them again.`,
        );
        return;
      }

      if (isMqttDirect && conversionsChanged) {
        try {
          await DevicesApi.setValueConversions(saved.id, parsedConversions.conversions);
        } catch (err) {
          onSaved();
          setError(`Device saved, but its value conversions weren't: ${err instanceof ApiError ? err.message : "unexpected error"}. Edit the device to set them again.`);
          return;
        }
      }

      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save device");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={isEdit ? `Edit ${device.name}` : "New device"} onClose={onClose} size="lg">
      <form onSubmit={handleSubmit} className="space-y-3">
        {!isEdit && (
          <div className="grid grid-cols-2 gap-3">
            {!fixedTenantId && (
              <FormField label="Tenant">
                <select
                  required
                  value={tenantId}
                  onChange={(e) => {
                    setTenantId(e.target.value);
                    setSiteId("");
                  }}
                  className={inputClass}
                >
                  <option value="">Select...</option>
                  {tenantsQuery.data?.items.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </FormField>
            )}

            <FormField label="Site">
              <select required value={siteId} onChange={(e) => setSiteId(e.target.value)} disabled={!tenantId} className={inputClass}>
                <option value="">{tenantId ? "Select..." : "Select a tenant first"}</option>
                {sitesQuery.data?.items.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            </FormField>

            <FormField label="Device type">
              <select required value={deviceTypeId} onChange={(e) => setDeviceTypeId(e.target.value)} className={inputClass}>
                <option value="">Select...</option>
                {deviceTypesQuery.data?.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </FormField>

            <FormField label="Connection">
              <div className="flex gap-4 text-sm py-1.5">
                <label className="flex items-center gap-1.5">
                  <input type="radio" checked={connection === "MqttDirect"} onChange={() => setConnection("MqttDirect")} /> MQTT direct
                </label>
                <label className="flex items-center gap-1.5">
                  <input type="radio" checked={connection === "Connector"} onChange={() => setConnection("Connector")} disabled={connectors.length === 0} /> Connector
                </label>
              </div>
              {connectors.length === 0 && <p className="text-xs text-muted-foreground">No connectors set up — devices send BMS-format MQTT.</p>}
            </FormField>
          </div>
        )}

        {!isEdit && connection === "MqttDirect" && (
          <FormField label="Device code">
            <input required value={deviceCode} onChange={(e) => setDeviceCode(e.target.value)} placeholder="e.g. device-021" className={inputClass} />
            <p className="text-xs text-muted-foreground mt-1">Unique; used in the device&apos;s MQTT topic (…/device-021/telemetry).</p>
          </FormField>
        )}

        {!isEdit && connection === "Connector" && (
          <div className="space-y-2">
            <div className="grid grid-cols-2 gap-3">
              <FormField label="Connector">
                <select
                  required
                  value={connectorId}
                  onChange={(e) => {
                    setConnectorId(e.target.value);
                    const picked = connectors.find((c) => c.id === e.target.value);
                    if (!deviceTypeId && picked?.defaultDeviceTypeId) setDeviceTypeId(picked.defaultDeviceTypeId);
                  }}
                  className={inputClass}
                >
                  <option value="">Select...</option>
                  {connectors.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name} · TCP {c.port}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Sensor ID (on the device label)">
                <input required value={sensorId} onChange={(e) => setSensorId(e.target.value)} placeholder="e.g. 100000005" className={inputClass} />
              </FormField>
            </div>
            <p className="text-xs text-muted-foreground">
              Device code: <code className="font-mono">{codeFromSensor || `${connector?.deviceCodePrefix ?? "prefix-"}<sensor ID>`}</code> (automatic)
            </p>
            {connector && chosenType && mappedKeys.length > 0 && (
              <p className={`text-xs ${connectorMismatch ? "text-destructive" : "text-emerald-600 dark:text-emerald-400"}`}>
                {connectorMismatch
                  ? `⚠ ${connector.name} sends ${mappedKeys.join(", ")} — ${chosenType.name} has none of these, so nothing would be stored.`
                  : `✓ ${matching} of ${typeKeys.length} ${chosenType.name} fields come from this connector`}
              </p>
            )}
          </div>
        )}

        {isEdit && (
          <p className="text-xs text-muted-foreground">
            Device code <code className="font-mono">{device.deviceCode}</code> ·{" "}
            {device.connection === "Connector"
              ? `connector ${connectors.find((c) => c.id === device.connectorId)?.name ?? ""}, sensor ${device.sensorId}`
              : "MQTT direct"}{" "}
            — fixed after creation.
          </p>
        )}

        <div className="grid grid-cols-2 gap-3">
          <FormField label="Name">
            <input required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. North wall tilt 1" className={inputClass} />
          </FormField>
          <FormField label="Model">
            <input value={model} onChange={(e) => setModel(e.target.value)} className={inputClass} />
          </FormField>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <FormField label="Serial number">
            <input value={serialNumber} onChange={(e) => setSerialNumber(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Manufacturer">
            <input value={manufacturer} onChange={(e) => setManufacturer(e.target.value)} className={inputClass} />
          </FormField>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <FormField label="Installed on (optional)">
            <input type="date" value={installedOn} onChange={(e) => setInstalledOn(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Mounting location (optional)">
            <input value={mountingLocation} onChange={(e) => setMountingLocation(e.target.value)} placeholder="e.g. B2, grid C4, east face" className={inputClass} />
          </FormField>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <FormField label="Latitude">
            <input type="number" step="any" value={latitude} onChange={(e) => setLatitude(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Longitude">
            <input type="number" step="any" value={longitude} onChange={(e) => setLongitude(e.target.value)} className={inputClass} />
          </FormField>
        </div>

        {chosenType?.tiltAxes && <TiltScheduleFields closingTime={tiltClosingTime} timeZoneId={tiltTimeZone} onTimeChange={setTiltClosingTime} onZoneChange={setTiltTimeZone} disabled={saving || (isEdit && tiltQuery.loading)} />}

        <div>
          <p className="block text-xs font-medium text-muted-foreground mb-1">Calculations</p>
          <DeviceCalculationsPicker
            tenantId={isEdit ? device.tenantId : tenantId}
            deviceTypeId={isEdit ? device.deviceTypeId : deviceTypeId}
            siteId={effectiveSiteId || undefined}
            deviceId={device?.id}
            value={calculationRuleIds}
            onChange={setCalculationRuleIds}
          />
        </div>

        {isMqttDirect && conversionType && (
          <ValueConversionsEditor parameters={conversionType.parameters} rows={conversionRows} onChange={setConversionRows} />
        )}

        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : isEdit ? "Save changes" : "Create device"}
        </button>
      </form>
    </Modal>
  );
}
