import json,time,urllib.request,urllib.error
from pathlib import Path
env=dict(line.split('=',1) for line in Path('/opt/protec/shared/cloud.env').read_text().splitlines() if '=' in line)
base='http://127.0.0.1:5000/api';token=None
def api(path,method='GET',body=None):
    headers={'Content-Type':'application/json'}
    if token:headers['Authorization']='Bearer '+token
    request=urllib.request.Request(base+path,method=method,headers=headers,data=None if body is None else json.dumps(body).encode())
    try:
        with urllib.request.urlopen(request,timeout=30) as response:
            text=response.read();return json.loads(text) if text else None
    except urllib.error.HTTPError as e:raise RuntimeError(path+': '+e.read().decode()) from e
auth=api('/auth/login','POST',{'email':env['BOOTSTRAP_ADMIN_EMAIL'],'password':env['BOOTSTRAP_ADMIN_PASSWORD']});token=auth['accessToken']
users=api('/users')
if len(users)!=1:raise RuntimeError('Fresh deployment must have only the bootstrap administrator before onboarding')
statefile=Path('/opt/protec/shared/onboarding.json')
if statefile.exists():print('Fresh Protec configuration already exists.');raise SystemExit(0)
company=api('/tenants','POST',{'name':'Protec Geosystems','slug':'protec-geosystems'})
site=api('/sites?tenantId='+company['id'],'POST',{'name':'Protec Site','code':'protec-site'})
device_type=api('/device-types','POST',{'name':'Inclinometer','description':'Three-axis inclination and acceleration, sensor temperature and battery (0–9), received as BW-NB-WM400 prefixed TCP CSV.','expectedIntervalSeconds':300,'supportsCommands':False,'tiltAxes':{'x':'xDeg','y':'yDeg','z':'zDeg'}})
parameters=[('xDeg','X-axis inclination','°'),('yDeg','Y-axis inclination','°'),('zDeg','Z-axis inclination','°'),('axG','X-axis acceleration','g'),('ayG','Y-axis acceleration','g'),('azG','Z-axis acceleration','g'),('temperature','Sensor temperature','°C'),('batteryLevel','Battery level','0–9')]
for key,label,unit in parameters:
    body={'key':key,'label':label,'unit':unit,'kind':'Instantaneous','dashboardWidget':'LiveValue'}
    if key=='batteryLevel':body.update(minValue=0,maxValue=9)
    api('/device-types/'+device_type['id']+'/parameters','POST',body)
mappings=[{'source':'1','target':'SensorId','parameterKey':None}]+[{'source':str(i+2),'target':'Parameter','parameterKey':p[0]} for i,p in enumerate(parameters)]+[{'source':'10','target':'Ignore','parameterKey':None}]
connector=api('/connectors','POST',{'name':'Protec Inclinometer - BW-NB-WM400 Prefix CSV','vendor':'BWSENSING','defaultDeviceTypeId':device_type['id'],'protocol':'Tcp','port':12000,'framing':'StartMarker','startMarker':'BW-NB-WM400,','csvFieldCount':10,'csvOptionalPrefix':True,'fixedLength':None,'endCharacter':None,'maxMessageBytes':1024,'format':'Csv','csvSeparator':',','pairSeparator':';','keyValueSeparator':'=','binaryHeaderHex':None,'binaryHeaderOffset':0,'binaryChecksum':'None','deviceCodePrefix':'protec-incl-','isEnabled':True,'mappings':mappings})
device=api('/devices?tenantId='+company['id'],'POST',{'siteId':site['id'],'deviceTypeId':device_type['id'],'connection':'Connector','connectorId':connector['id'],'sensorId':'100000005','deviceCode':'protec-incl-100000005','name':'Inclinometer 100000005','serialNumber':'100000005','manufacturer':'BWSENSING','model':'BW-NB-WM400'})
daily=api('/devices/'+device['id']+'/tilt','PUT',{'closingTime':'23:59','timeZoneId':'Asia/Singapore'})
state={'company':company,'site':site,'deviceType':device_type,'connector':connector,'device':device,'freshDatabase':True,'dailyCutoff':'23:59','timeZone':'Asia/Singapore'}
statefile.write_text(json.dumps(state,indent=2));statefile.chmod(0o600)
print(json.dumps({'company':company['name'],'site':site['name'],'deviceId':device['id'],'connectorPort':12000,'freshDatabase':True,'dailyCutoff':'23:59 Asia/Singapore'}))
