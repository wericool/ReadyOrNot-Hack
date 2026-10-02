-- RoN Solo ESP: reflected functions verified on Steam build 24942528 / UE 5.3.
local output = '__RON_ESP_TELEMETRY_PATH__'
local seq, pending, actors, refresh = 0, false, {}, 60
local cachedPC, pcRefresh, lastWorld = nil, 60, 0
local lastError = ''
local boneCache={}
local settingsPath=output:gsub('telemetry%.json$','settings.json')
local settingsReadAt=-1
local options={enabled=true,civilians=true,arrested=false,inactive=false,skeleton=true,evidence=true,reports=true,range=150,updateMs=16}
local function loadOptions()
 local f=io.open(settingsPath,'r')
 if not f then return end
 local data=f:read('*a');f:close()
 for _,key in ipairs({'enabled','civilians','arrested','inactive','skeleton','evidence','reports'}) do
  local value=data:match('"'..key..'"%s*:%s*(%a+)')
  if value=='true' or value=='false' then options[key]=value=='true' end
 end
 local requested=tonumber(data:match('"updateMs"%s*:%s*(%d+)'))
 if requested==8 or requested==16 or requested==33 or requested==50 or requested==100 then options.updateMs=requested end
 options.range=math.max(25,math.min(500,tonumber(data:match('"range"%s*:%s*(%d+)')) or options.range))
end
local evidenceActors,reportActors,incapacitatedActors={},{},{}
local boneAliases={{'Head','head'},{'neck_1','neck_01'},{'spine_3','spine_03'},{'pelvis'},
 {'upperarm_LE','upperarm_l'},{'lowerarm_LE','lowerarm_l'},{'hand_LE','hand_l'},
 {'upperarm_RI','upperarm_r'},{'lowerarm_RI','lowerarm_r'},{'hand_RI','hand_r'},
 {'thigh_LE','thigh_l'},{'calf_LE','calf_l'},{'foot_LE','foot_l'},
 {'thigh_RI','thigh_r'},{'calf_RI','calf_r'},{'foot_RI','foot_r'}}
local function valid(o) return o and o:IsValid() end
local function belongsTo(actor,world)
 local level=actor:GetLevel()
 if not valid(level) then return false end
 local owning=level.OwningWorld
 return valid(owning) and owning:GetAddress()==world:GetAddress()
end
local function project(pc,loc)
 local screen={}
 local ok=pc:ProjectWorldLocationToScreen(loc,screen,false)
 screen=screen.ScreenLocation or screen
 if ok and screen.X and screen.Y then return screen end
end
local function skeleton(pc,actor,loc,overrideMesh)
 local mesh=overrideMesh or actor.Mesh
 if not valid(mesh) then return '[]' end
 local address=mesh:GetAddress()
 local names=boneCache[address]
 if not names then
  names={}; local byName={}
  for i=0,mesh:GetNumBones()-1 do
   local name=mesh:GetBoneName(i)
   byName[name:ToString():lower()]=name
  end
  for j,aliases in ipairs(boneAliases) do
   for _,alias in ipairs(aliases) do
    if byName[alias:lower()] then names[j]=byName[alias:lower()];break end
   end
  end
  boneCache[address]=names
 end
 local points={}
 for i=1,#boneAliases do
  local p=nil
  if names[i] then
   local world=mesh:GetSocketLocation(names[i])
   local dx,dy,dz=world.X-loc.X,world.Y-loc.Y,world.Z-loc.Z
   if dx*dx+dy*dy+dz*dz<400*400 then p=project(pc,world) end
  end
  points[i]=p and string.format('{"x":%.2f,"y":%.2f,"valid":true}',p.X,p.Y) or '{"valid":false}'
 end
 return '['..table.concat(points,',')..']'
end
local function quote(value)
 return '"'..tostring(value):gsub('[%z\1-\31\\"]',function(c)
  if c=='\\' then return '\\\\' end
  if c=='"' then return '\\"' end
  return string.format('\\u%04x',string.byte(c))
 end)..'"'
end
local function readableName(actor,field,fallback)
 local ok,value=pcall(function() return actor[field]:ToString() end)
 if ok and value and value~='' then return value end
 return fallback
end
local function emit(status,w,h,total,boxes,civilians,items,evidence,reports)
 seq=seq+1
 local f=io.open(output,'w')
 if not f then return end
 f:write(string.format('{"updateMs":%d,"seq":%d,"status":"%s","width":%d,"height":%d,"total":%d,"civilians":%d,"evidence":%d,"reports":%d,"boxes":[%s],"items":[%s]}',options.updateMs,seq,status,w or 0,h or 0,total or 0,civilians or 0,evidence or 0,reports or 0,table.concat(boxes or {},','),table.concat(items or {},',')))
 f:close()
end
local function update()
 local now=os.time()
 if pcRefresh~=now or not valid(cachedPC) then
  cachedPC=nil; pcRefresh=now
  for _,p in ipairs(FindAllOf('PlayerController') or {}) do
   if valid(p) and p:IsLocalPlayerController() then cachedPC=p; break end
  end
 end
 local pc=cachedPC
 if not pc then emit('no_pawn'); return end
 local level=pc:GetLevel()
 local world=valid(level) and level.OwningWorld or nil
 if not valid(world) then emit('no_pawn'); return end
 if world:GetAddress()~=lastWorld then actors={};boneCache={};evidenceActors={};reportActors={};incapacitatedActors={};refresh=-1;lastWorld=world:GetAddress() end
 if valid(world.NetDriver) then emit('solo_only'); return end
 local pawn=pc.Pawn
 if not valid(pawn) then emit('no_pawn'); return end
 local sx,sy={0},{0}
 pc:GetViewportSize(sx,sy)
 local width,height=sx.SizeX,sx.SizeY
 if not width or not height or width<=0 or height<=0 then emit('viewport'); return end
 if refresh~=now then
  actors=FindAllOf('ReadyOrNotCharacter') or {}
  evidenceActors={}
  for _,component in ipairs(FindAllOf('EvidenceComponent') or {}) do
   if valid(component) then
    local owner=component:GetOwner()
    if valid(owner) and belongsTo(owner,world) and component:CanBeCollected() and not component:IsEvidenceCollected() and not component.bEvidenceExtracted then
     evidenceActors[#evidenceActors+1]={actor=owner,component=component,name=readableName(owner,'EvidenceName',readableName(owner,'ItemName','Оружие / улика'))}
    end
   end
  end
  reportActors=FindAllOf('ReportableActor') or {}
  incapacitatedActors=FindAllOf('IncapacitatedHuman') or {}
  refresh=now
 end
 local camera=pc.PlayerCameraManager
 if not valid(camera) then emit('no_camera'); return end
 local eye=camera:GetCameraLocation()
 local boxes,total,civilians={},0,0
 for _,actor in ipairs(actors) do
  if valid(actor) and belongsTo(actor,world) and (actor:IsSuspect() or actor:IsCivilian()) then
   local kind=actor:IsSuspect() and 'suspect' or 'civilian'
   local state=actor:IsDeadNotUnconscious() and 'dead' or actor:IsUnconsciousNotDead() and 'unconscious' or actor:IsArrested() and 'arrested' or actor:IsSurrendered() and 'surrendered' or 'active'
   if state=='active' or state=='surrendered' then
    if kind=='suspect' then total=total+1 else civilians=civilians+1 end
   end
   local loc=actor:K2_GetActorLocation()
   local dx,dy,dz=loc.X-eye.X,loc.Y-eye.Y,loc.Z-eye.Z
   local distance=math.sqrt(dx*dx+dy*dy+dz*dz)/100
   if options.enabled and (kind~='civilian' or options.civilians)
    and (state~='arrested' or options.arrested)
    and ((state~='dead' and state~='unconscious') or options.inactive)
    and distance>0.5 and distance<=options.range then
    local cap=actor.CapsuleComponent
    local half=90
    if valid(cap) then half=cap:GetScaledCapsuleHalfHeight() end
    local top,bottom={},{}
    local a=pc:ProjectWorldLocationToScreen({X=loc.X,Y=loc.Y,Z=loc.Z+half},top,false)
    local b=pc:ProjectWorldLocationToScreen({X=loc.X,Y=loc.Y,Z=loc.Z-half},bottom,false)
    top=top.ScreenLocation or top
    bottom=bottom.ScreenLocation or bottom
    if a and b and top.X and bottom.X then
     local heightBox=math.abs(bottom.Y-top.Y)
     local widthBox=heightBox*0.48
     local x=(top.X+bottom.X)/2-widthBox/2
     local y=math.min(top.Y,bottom.Y)
     if heightBox>=2 and heightBox<height*3 and x+widthBox>=0 and x<=width and y+heightBox>=0 and y<=height then
      local ok,bones=true,'[]'
      if options.skeleton then ok,bones=pcall(skeleton,pc,actor,loc) end
      if not ok then
       if tostring(bones)~=lastError then print('[RoNESP] Bones: '..tostring(bones)..'\n');lastError=tostring(bones) end
       bones='[]'
      end
      boxes[#boxes+1]=string.format('{"x":%.2f,"y":%.2f,"w":%.2f,"h":%.2f,"d":%.2f,"kind":"%s","state":"%s","hp":%.2f,"maxhp":%.2f,"bones":%s}',x,y,widthBox,heightBox,distance,kind,state,actor:GetCurrentHealth(),actor:GetMaxHealth(),bones)
     end
    end
   end
  end
 end
 local items,evidence,reports={},0,0
 local function mark(actor,kind,name,mesh)
  local loc=valid(mesh) and mesh:K2_GetComponentLocation() or actor:K2_GetActorLocation()
  local dx,dy,dz=loc.X-eye.X,loc.Y-eye.Y,loc.Z-eye.Z
  local distance=math.sqrt(dx*dx+dy*dy+dz*dz)/100
  if options.enabled and distance>0.5 and distance<=options.range then
   local point=project(pc,loc)
   if point and point.X>=0 and point.X<=width and point.Y>=0 and point.Y<=height then
    local bones='[]'
    if options.skeleton and valid(mesh) then local ok,value=pcall(skeleton,pc,actor,loc,mesh);if ok then bones=value end end
    items[#items+1]=string.format('{"x":%.2f,"y":%.2f,"d":%.2f,"kind":"%s","name":%s,"bones":%s}',point.X,point.Y,distance,kind,quote(name),bones)
   end
  end
 end
 for _,entry in ipairs(evidenceActors) do
  local a,c=entry.actor,entry.component
  if valid(a) and valid(c) and c:CanBeCollected() and not c:IsEvidenceCollected() and not c.bEvidenceExtracted then
   evidence=evidence+1;if options.evidence then mark(a,'evidence',entry.name) end
  end
 end
 for _,a in ipairs(reportActors) do
  if valid(a) and belongsTo(a,world) and a.bReportableEnabled and not a.bHasBeenReported then
   reports=reports+1;if options.reports then mark(a,'report',readableName(a,'ReportableName','Объект для доклада')) end
  end
 end
 for _,a in ipairs(incapacitatedActors) do
  if valid(a) and belongsTo(a,world) and not a:HasBeenReported() then
   reports=reports+1
   if options.reports then mark(a,'report',a.bIsDead and 'Пострадавший: мёртв' or 'Пострадавший: ранен',a.HumanMesh) end
  end
 end
 emit('ready',width,height,total,boxes,civilians,items,evidence,reports)
end
print('[RoNESP] Solo telemetry started\n')
local startLoop
startLoop=function(interval)
 LoopAsync(interval,function()
  local now=os.time()
  if settingsReadAt~=now then pcall(loadOptions);settingsReadAt=now end
  if options.updateMs~=interval then
   startLoop(options.updateMs)
   return true
  end
  if pending then return false end
  pending=true
  ExecuteInGameThread(function()
   local ok,err=pcall(update)
   pending=false
   if not ok then
    emit('error')
    err=tostring(err)
    if err~=lastError then print('[RoNESP] '..err..'\n');lastError=err end
   end
  end)
  return false
 end)
end
pcall(loadOptions)
startLoop(options.updateMs)
