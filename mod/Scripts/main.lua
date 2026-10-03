-- Ready or Not hack by ericool 1.0: ESP and gameplay controls.
local output = '__RON_ESP_TELEMETRY_PATH__'
local settingsPath=output:gsub('telemetry%.json$','settings.json')
local pipeName='\\\\.\\pipe\\RoNUnifiedESP_Fast_v1'
local options={enabled=true,civilians=true,arrested=false,inactive=false,skeleton=true,headDot=false,ammo=true,traps=true,evidence=true,reports=true,health=true,healthbar=true,freecam=false,noRetaliation=false,stationFire=false,range=150,updateMs=16,bonesMs=100,healthMs=250}
local actors,evidenceActors,reportActors,incapacitatedActors={},{},{},{}
local cache,boneNames={},{}
local doorActors={}
local pc,lastWorld,discoveryAt=nil,0,-1
local seq,pending,payload=0,false,nil
local pipe,settingsAt=nil,-1
local collectMs,bonesCostMs,healthCostMs=0,0,0
local projectionCheckAt=-1
local aliases={{'Head','head'},{'neck_1','neck_01'},{'spine_3','spine_03'},{'pelvis'},{'upperarm_LE','upperarm_l'},{'lowerarm_LE','lowerarm_l'},{'hand_LE','hand_l'},{'upperarm_RI','upperarm_r'},{'lowerarm_RI','lowerarm_r'},{'hand_RI','hand_r'},{'thigh_LE','thigh_l'},{'calf_LE','calf_l'},{'foot_LE','foot_l'},{'thigh_RI','thigh_r'},{'calf_RI','calf_r'},{'foot_RI','foot_r'}}
local function valid(o) return o and o:IsValid() end
local function clean(v) return tostring(v):gsub('[\t\r\n]',' ') end
local function loadOptions()
 local f=io.open(settingsPath,'r');if not f then return end
 local data=f:read('*a');f:close()
 for _,key in ipairs({'enabled','civilians','arrested','inactive','skeleton','headDot','ammo','traps','evidence','reports','health','healthbar','freecam','noRetaliation','stationFire'}) do
  local value=data:match('"'..key..'"%s*:%s*(%a+)')
  if value=='true' or value=='false' then options[key]=value=='true' end
 end
 for _,key in ipairs({'range','updateMs','bonesMs','healthMs'}) do
  local value=tonumber(data:match('"'..key..'"%s*:%s*(%d+)'))
  if value then options[key]=value end
 end
 options.range=math.max(25,math.min(500,options.range))
 options.updateMs=math.max(8,math.min(100,options.updateMs))
 options.bonesMs=math.max(33,math.min(500,options.bonesMs))
 options.healthMs=math.max(100,math.min(1000,options.healthMs))
end
local function belongs(actor,world)
 local level=actor:GetLevel();local owner=valid(level) and level.OwningWorld or nil
 return valid(owner) and owner:GetAddress()==world:GetAddress()
end
local freeCamera=nil
local stationChanges={}
local stationScanAt=-1
local stationInputPath=settingsPath:gsub('settings%.json$','station-input.txt')
local inputSession,inputSeq,inputPawn=nil,0,nil
local inputPacket,inputPending=nil,false
local inputRangeWorld,inputRangeAreas=0,{}
local function traceStationInput(reason,details)
 local f=io.open(settingsPath:gsub('settings%.json$','station-input-log.txt'),'a')
 if f then f:write(os.date('%H:%M:%S'),' seq=',tostring(inputSeq),' ',reason,' ',details or'','\n');f:close()end
end
local function clearStationActionLowReady(pawn)
 pawn.bForceLowReady=false;pawn.bUserLowReady=false
 pawn.bLowReadyPointDown=false;pawn.bLowReadyPointUp=false
end
local function stopStationInput()
 if inputPawn then
  local previous=inputPawn;inputPawn=nil
  if valid(previous)then local ok,e=pcall(function()
   -- The station blueprint can rewrite these between telemetry updates.
   if options.stationFire then clearStationActionLowReady(previous)end
   previous:EndPrimaryUse()
  end);traceStationInput('release',ok and'ok'or tostring(e))end
 end
end
local function applyStationInput(data)
 local session,stamp,enabled,sequence=data:match('^(%x+) (%d+) ([01]) (%d+) [01]\n')
 sequence=tonumber(sequence)
 if session and session~=inputSession then stopStationInput();inputSession=session;inputSeq=sequence or 0;traceStationInput('new session');return end
 local world=valid(pc)and valid(pc:GetLevel())and pc:GetLevel().OwningWorld or nil
 local gs=valid(world)and world.GameState or nil
 local pawn=valid(pc)and pc.Pawn or nil
 local age=stamp and os.time()-tonumber(stamp)or 100
 if enabled~='1'or age<0 or age>1 or not options.stationFire or options.freecam or not pipe or not valid(world)or not valid(gs)or not gs:IsA(StaticFindObject('/Script/ReadyOrNot.LobbyGS'))or not valid(pawn)then
  if sequence and sequence>inputSeq then traceStationInput('gated','enabled='..tostring(enabled)..' age='..age..' option='..tostring(options.stationFire)..' camera='..tostring(options.freecam)..' pipe='..tostring(pipe~=nil)..' world='..tostring(valid(world))..' pawn='..tostring(valid(pawn)))end
  stopStationInput();if sequence then inputSeq=math.max(inputSeq,sequence)end;return
 end
 if inputPawn and inputPawn:GetAddress()~=pawn:GetAddress()then stopStationInput();inputSeq=sequence or inputSeq;return end
 if world:GetAddress()~=inputRangeWorld then inputRangeWorld=world:GetAddress();inputRangeAreas=FindAllOf('LobbyFiringRangeArea')or{}end
 for line in data:gmatch('[^\r\n]+')do
  local number,state=line:match('^(%d+) ([01])$')
  number=tonumber(number)
  if number and number>inputSeq then
   inputSeq=number
   if state=='0'then stopStationInput()
   elseif not inputPawn then
    -- Inside the regular range, the game's own input remains responsible.
    local inRange=false
    for _,area in ipairs(inputRangeAreas)do if valid(area)and belongs(area,world)and area:IsOverlappingActor(pawn)then inRange=true;break end end
    if not inRange then
     traceStationInput('press','pawn='..pawn:GetFullName())
     clearStationActionLowReady(pawn)
     pawn:PrimaryUse();inputPawn=pawn
     local item=pawn:GetEquippedItem()
     local ok,details=pcall(function()return 'item='..(valid(item)and item:GetFullName()or'none')..' force='..tostring(pawn.bForceLowReady)..' user='..tostring(pawn.bUserLowReady)..' down='..tostring(pawn.bLowReadyPointDown)..' actionsLocked='..tostring(pawn.bActionsLocked)..' grenadeCanThrow='..(valid(item)and tostring(item.bCanThrowGrenade)or'none')end)
     traceStationInput('returned',ok and details or tostring(details))
    else traceStationInput('range skip')end
   end
  end
 end
end
local function restoreStation()
 stopStationInput()
 for _,change in ipairs(stationChanges)do
  if valid(change.object)then
   if change.kind=='volume'then
    change.object.bForceLowReadyWhileInside=change.inside
    change.object.bForceLowReadyWhileAimingAt=change.aim
    change.object:SetActorEnableCollision(change.collision)
   elseif change.kind=='weapon'then change.object.bUseLowReady=change.use
   elseif change.kind=='pawn'then
    change.object.LowReadyTraceDistance=change.trace
    change.object.bForceLowReady=change.force;change.object.bUserLowReady=change.user
    change.object.bLowReadyPointDown=change.down;change.object.bLowReadyPointUp=change.up
   end
  end
 end
 stationChanges={};stationScanAt=-1
end
local function restoreGameplay()
 restoreStation()
end
local function updateGameplay(world)
 local original=freeCamera and freeCamera.controller or pc
 if not valid(original)or not valid(world)then restoreGameplay();return end
 local gs=world.GameState
 if not options.stationFire or not valid(gs)or not gs:IsA(StaticFindObject('/Script/ReadyOrNot.LobbyGS'))then restoreStation();return end
 local pawn=original.Pawn
 if valid(pawn)then
  local saved=false
  for _,change in ipairs(stationChanges)do if change.kind=='pawn'and change.address==pawn:GetAddress()then saved=true;break end end
  if not saved then
   stationChanges[#stationChanges+1]={object=pawn,address=pawn:GetAddress(),kind='pawn',trace=pawn.LowReadyTraceDistance,force=pawn.bForceLowReady,user=pawn.bUserLowReady,down=pawn.bLowReadyPointDown,up=pawn.bLowReadyPointUp}
   pawn.LowReadyTraceDistance=0
  end
  pawn.bForceLowReady=false;pawn.bUserLowReady=false;pawn.bLowReadyPointDown=false;pawn.bLowReadyPointUp=false
 end
 local second=os.time();if stationScanAt==second then return end;stationScanAt=second
 local function remember(object,kind)
  local address=object:GetAddress()
  for _,change in ipairs(stationChanges)do if change.address==address then return false end end
  local change={object=object,address=address,kind=kind}
  if kind=='volume'then
   change.inside=object.bForceLowReadyWhileInside;change.aim=object.bForceLowReadyWhileAimingAt;change.collision=object:GetActorEnableCollision()
   stationChanges[#stationChanges+1]=change
   object.bForceLowReadyWhileInside=false;object.bForceLowReadyWhileAimingAt=false;object:SetActorEnableCollision(false)
  elseif kind=='weapon'then
   change.use=object.bUseLowReady;stationChanges[#stationChanges+1]=change;object.bUseLowReady=false
  elseif kind=='pawn'then
   change.trace=object.LowReadyTraceDistance;stationChanges[#stationChanges+1]=change;object.LowReadyTraceDistance=0
  end
  return true
 end
 for _,volume in ipairs(FindAllOf('ForceLowReadyVolume')or{})do if valid(volume)and belongs(volume,world)then remember(volume,'volume')end end
 if valid(original.Pawn)then
  local pawn=original.Pawn;local weapon=pawn:GetEquippedWeapon()
  local changed=remember(pawn,'pawn')
  if valid(weapon)then changed=remember(weapon,'weapon')or changed end
  if changed then pawn:SetForceLowReady(false);pawn:Server_SetUserLowReady(false);pawn:Server_SetLowReady(false,false,false)end
 end
end
local function stopFreecam()
 if not freeCamera then return end
 local previous=freeCamera
 if valid(previous.camera)and valid(previous.controller)then
  local manager=previous.camera.CheatManager
  if not valid(manager)then manager=StaticConstructObject(StaticFindObject('/Script/Engine.CheatManager'),previous.camera);previous.camera.CheatManager=manager end
  assert(valid(manager),'Debug camera return manager unavailable')
  manager:DisableDebugCamera();pc=previous.controller
 end
 freeCamera=nil
end
local function updateFreecam(world)
 if not options.freecam or not valid(world)then stopFreecam();return end
 if freeCamera then
  if freeCamera.world~=world:GetAddress()or not valid(freeCamera.controller)then stopFreecam();return end
  if valid(freeCamera.camera)then pc=freeCamera.camera end
  return
 end
 if not valid(pc)or not valid(pc.Pawn)then return end
 local original=pc;local manager=original.CheatManager
 if not valid(manager)then
  local class=StaticFindObject('/Script/Engine.CheatManager')
  assert(valid(class),'CheatManager class unavailable')
  manager=StaticConstructObject(class,original)
  assert(valid(manager),'CheatManager creation failed')
  original.CheatManager=manager
 end
 manager:EnableDebugCamera()
 local camera=manager.DebugCameraControllerRef
 assert(valid(camera),'Debug camera was not created')
 freeCamera={controller=original,manager=manager,camera=camera,world=world:GetAddress()}
 if not valid(camera.CheatManager)then camera.CheatManager=StaticConstructObject(StaticFindObject('/Script/Engine.CheatManager'),camera)end
 camera.bShowSelectedInfo=false
 camera:SetPawnMovementSpeedScale(1)
 pc=camera
end
local function name(actor,key,fallback)
 local ok,value=pcall(function()return actor[key]:ToString()end)
 return ok and value and value~='' and clean(value) or fallback
end
local function project(loc)
 local screen={};local ok=pc:ProjectWorldLocationToScreen(loc,screen,false);screen=screen.ScreenLocation or screen
 if ok and screen.X then return screen end
end
local function dot(v,x,y,z)return v.X*x+v.Y*y+v.Z*z end
local function makeCamera(camera,width,height)
 local eye=camera:GetCameraLocation();local rot=camera:GetCameraRotation()
 local p,y,r=math.rad(rot.Pitch),math.rad(rot.Yaw),math.rad(rot.Roll)
 local cp,sp,cy,sy,cr,sr=math.cos(p),math.sin(p),math.cos(y),math.sin(y),math.cos(r),math.sin(r)
 local forward={X=cp*cy,Y=cp*sy,Z=sp}
 local right={X=sr*sp*cy-cr*sy,Y=sr*sp*sy+cr*cy,Z=-sr*cp}
 local up={X=-cr*sp*cy-sr*sy,Y=-cr*sp*sy+sr*cy,Z=cr*cp}
 local function probe(a,b)return {X=eye.X+forward.X*1000+right.X*a+up.X*b,Y=eye.Y+forward.Y*1000+right.Y*a+up.Y*b,Z=eye.Z+forward.Z*1000+right.Z*a+up.Z*b}end
 local center,px,py=project(probe(0,0)),project(probe(500,0)),project(probe(0,500))
 if not center or not px or not py then return end
 local c={eye=eye,f=forward,r=right,u=up,cx=center.X,cy=center.Y,fx=(px.X-center.X)*2,fy=(center.Y-py.Y)*2}
 local values={eye.X,eye.Y,eye.Z,forward.X,forward.Y,forward.Z,right.X,right.Y,right.Z,up.X,up.Y,up.Z,c.cx,c.cy,c.fx,c.fy}
 for i,v in ipairs(values)do values[i]=string.format('%.6f',v)end
 c.wire=table.concat(values,'\t')
 return c
end
local function projected(c,loc)
 local x,y,z=loc.X-c.eye.X,loc.Y-c.eye.Y,loc.Z-c.eye.Z
 local depth=dot(c.f,x,y,z);if depth<=1 then return end
 return c.cx+dot(c.r,x,y,z)*c.fx/depth,c.cy-dot(c.u,x,y,z)*c.fy/depth
end
local function getBoneNames(mesh)
 if not valid(mesh)then return end
 local id=mesh:GetAddress();local names=boneNames[id]
 if not names then
  names={};local lookup={}
  for i=0,mesh:GetNumBones()-1 do local n=mesh:GetBoneName(i);lookup[n:ToString():lower()]=n end
  for i,list in ipairs(aliases)do for _,alias in ipairs(list)do if lookup[alias:lower()]then names[i]=lookup[alias:lower()];break end end end
  boneNames[id]=names
 end
 return names
end
local function getHead(mesh,loc)
 local names=getBoneNames(mesh);if not names or not names[1]then return end
 local pos=mesh:GetSocketLocation(names[1]);if not pos then return end
 local x,y,z=pos.X-loc.X,pos.Y-loc.Y,pos.Z-loc.Z
 if x*x+y*y+z*z>160000 then return end
 return pos
end
local function getBones(mesh,loc)
 local names=getBoneNames(mesh);if not names then return end
 local fields={}
 for i=1,16 do
  local pos=names[i] and mesh:GetSocketLocation(names[i]) or nil
  if pos then local x,y,z=pos.X-loc.X,pos.Y-loc.Y,pos.Z-loc.Z;if x*x+y*y+z*z>160000 then pos=nil end end
  fields[#fields+1]=pos and string.format('%.2f\t%.2f\t%.2f',pos.X,pos.Y,pos.Z)or '_\t_\t_'
 end
 return table.concat(fields,'\t')
end
local function skeleton(lines,id,actor,loc,mesh,now)
 local entry=cache[id];if not entry then entry={};cache[id]=entry end
 if options.skeleton and (not entry.bonesAt or now>=entry.bonesAt)then
  local t=os.clock();local value=getBones(mesh or actor.Mesh,loc);bonesCostMs=bonesCostMs+(os.clock()-t)*1000
  entry.bonesAt=now+options.bonesMs/1000
  if value then lines[#lines+1]='S\t'..id..'\t'..value end
 end
end
local function statusFrame(status)
 seq=seq+1
 payload=string.format('F\t%d\t%s\t%s\t0\t0\t0\t0\t0\t0\t%d\t0\t0\t0\t',seq,tostring(lastWorld),status,options.updateMs)..'0\t0\t0\t1\t0\t0\t0\t1\t0\t0\t0\t1\t0\t0\t1\t1\nE\n'
end
local function update()
 local started=os.clock();local now=os.clock();local second=os.time()
 bonesCostMs,healthCostMs=0,0
 if not valid(pc) then
  for _,p in ipairs(FindAllOf('ReadyOrNotPlayerController')or{})do if valid(p)and p:IsLocalPlayerController()then pc=p;break end end
 end
 if not valid(pc)then statusFrame('no_pawn');return end
 local level=pc:GetLevel();local world=valid(level)and level.OwningWorld or nil
 if not valid(world)then statusFrame('no_pawn');return end
 if world:GetAddress()~=lastWorld then lastWorld=world:GetAddress();cache={};boneNames={};discoveryAt=-1 end
 updateGameplay(world)
 updateFreecam(world)
 if not valid(pc.Pawn)and not(freeCamera and valid(freeCamera.controller.Pawn))then statusFrame('no_pawn');return end
 local sx,sy={0},{0};pc:GetViewportSize(sx,sy);local width,height=sx.SizeX,sx.SizeY
 if not width or width<1 or not height or height<1 then statusFrame('viewport');return end
 local manager=pc.PlayerCameraManager;if not valid(manager)then statusFrame('no_camera');return end
 local camera=makeCamera(manager,width,height);if not camera then statusFrame('projection');return end
 if discoveryAt~=second then
  local retained={}
  actors={}
  for _,a in ipairs(FindAllOf('ReadyOrNotCharacter')or{})do
   if valid(a)and belongs(a,world)and(a:IsSuspect()or a:IsCivilian())then
    local id=tostring(a:GetAddress());local entry=cache[id]or{}
    entry.actor=a;entry.id=id;entry.kind=a:IsSuspect()and'suspect'or'civilian'
    local cap=a.CapsuleComponent;entry.half=valid(cap)and cap:GetScaledCapsuleHalfHeight()or 90
    retained[id]=entry;actors[#actors+1]=entry
   end
  end
  evidenceActors={}
  for _,component in ipairs(FindAllOf('EvidenceComponent')or{})do
   if valid(component)then local a=component:GetOwner()
    if valid(a)and belongs(a,world)then evidenceActors[#evidenceActors+1]={actor=a,component=component,name=name(a,'EvidenceName',name(a,'ItemName','Оружие / улика'))}end
   end
  end
  doorActors={}
  if options.traps then for _,door in ipairs(FindAllOf('Door')or{})do if valid(door)and belongs(door,world)then doorActors[#doorActors+1]={actor=door}end end end
  reportActors=FindAllOf('ReportableActor')or{}
  incapacitatedActors=FindAllOf('IncapacitatedHuman')or{}
  for _,a in ipairs(incapacitatedActors)do if valid(a)then local id=tostring(a:GetAddress());retained[id]=cache[id]or{} end end
  cache=retained;discoveryAt=second
 end
 local gs=world.GameState
 local station=valid(gs)and gs:IsA(StaticFindObject('/Script/ReadyOrNot.LobbyGS'))
 local lines,total,civilians,evidence,reports={'G\t'..(valid(world.NetDriver)and'0'or'1')..'\t'..(station and'1'or'0')},0,0,0,0
 if options.ammo then
  local ok,count=pcall(function()
   local player=freeCamera and freeCamera.controller or pc
   local pawn=valid(player)and player.Pawn or nil
   local weapon=valid(pawn)and pawn:GetEquippedItem()or nil
   if not valid(weapon)or not weapon:IsA(StaticFindObject('/Script/ReadyOrNot.BaseMagazineWeapon'))then return end
   local index=tonumber(weapon.MagIndex)
   if not index or index<0 or index>=weapon:GetMagazineCount()then return end
   local count=tonumber(weapon:GetAmmoInMagazine(index))
   if count and count==count and count>=0 and count<=10000 then return math.floor(count+0.5)end
  end)
  if ok and count then lines[#lines+1]='A\t'..count end
 end
 local projectionError,projectionChecks=0,0
 for _,entry in ipairs(actors)do
  local a=entry.actor
  if valid(a)then
   if not entry.stateAt or now>=entry.stateAt then
    local t=os.clock()
    entry.state=a:IsDeadNotUnconscious()and'dead'or a:IsUnconsciousNotDead()and'unconscious'or a:IsArrested()and'arrested'or a:IsSurrendered()and'surrendered'or'active'
    entry.needReport,entry.canCuff=false,false
    if entry.state=='dead'or entry.state=='unconscious'then
     entry.needReport=not a:HasBeenReported()
     entry.canCuff=a:CanArrestRagdoll()and not a:IsArrested()
    end
    entry.hp=(options.health or options.healthbar)and a:GetCurrentHealth()or 0
    entry.maxhp=(options.health or options.healthbar)and a:GetMaxHealth()or 0
    entry.stateAt=now+options.healthMs/1000
    healthCostMs=healthCostMs+(os.clock()-t)*1000
   end
   local state=entry.state
   if state=='active'or state=='surrendered'then if entry.kind=='suspect'then total=total+1 else civilians=civilians+1 end end
   if options.enabled and(entry.kind~='civilian'or options.civilians)and(state~='arrested'or options.arrested)and((state~='dead'and state~='unconscious')or options.inactive)then
    local loc=a:K2_GetActorLocation();local x,y,z=loc.X-camera.eye.X,loc.Y-camera.eye.Y,loc.Z-camera.eye.Z;local distance=math.sqrt(x*x+y*y+z*z)/100
    if distance>.5 and distance<=options.range then
     local px,py=projected(camera,loc)
     if px and px>-width and px<width*2 and py>-height and py<height*2 then
      lines[#lines+1]=string.format('B\t%s\t%.2f\t%.2f\t%.2f\t%.2f\t%.2f\t%s\t%s\t%.2f\t%.2f',entry.id,loc.X,loc.Y,loc.Z,entry.half,distance,entry.kind,state,entry.hp,entry.maxhp)
      if options.headDot and entry.kind=='suspect'then
       local ok,head=pcall(getHead,a.Mesh,loc)
       if ok and head then lines[#lines+1]=string.format('H\t%s\t%.2f\t%.2f\t%.2f',entry.id,head.X,head.Y,head.Z)end
      end
      local ok,err=pcall(skeleton,lines,entry.id,a,loc,nil,now)
      if not ok then entry.bonesAt=now+1 end
      -- One validation per discovery second, not one Unreal projection per joint.
      if projectionChecks==0 and projectionCheckAt~=second then
       projectionCheckAt=second;local actual=project(loc)
       if actual then projectionError=math.sqrt((actual.X-px)^2+(actual.Y-py)^2);projectionChecks=1 end
      end
     end
    end
   end
  end
 end
 local function mark(a,kind,label,mesh)
  local loc=valid(mesh)and mesh:K2_GetComponentLocation()or a:K2_GetActorLocation()
  if kind=='body'and valid(mesh)then
   local ok,pelvis=pcall(function()local names=getBoneNames(mesh);return names and names[4]and mesh:GetSocketLocation(names[4])or nil end)
   if ok and pelvis then loc=pelvis end
  end
  local x,y,z=loc.X-camera.eye.X,loc.Y-camera.eye.Y,loc.Z-camera.eye.Z;local distance=math.sqrt(x*x+y*y+z*z)/100
  if options.enabled and(distance>.5 or kind=='trap')and distance<=options.range then
   local px,py=projected(camera,loc)
   if px and px>=0 and px<=width and py>=0 and py<=height then
    local id=tostring(a:GetAddress());lines[#lines+1]=string.format('M\t%s\t%.2f\t%.2f\t%.2f\t%.2f\t%s\t%s',id,loc.X,loc.Y,loc.Z,distance,kind,clean(label))
    if valid(mesh)then pcall(skeleton,lines,id,a,loc,mesh,now)end
   end
  end
 end
 for _,entry in ipairs(actors)do
  if valid(entry.actor)and(entry.state=='dead'or entry.state=='unconscious')and(entry.needReport or entry.canCuff)then
   reports=reports+1
   if options.reports then
    local label=(entry.kind=='suspect'and'Подозреваемый'or'Гражданский')..(entry.state=='dead'and': мёртв'or': без сознания')
    if entry.needReport then label=label..' · нужен рапорт'end
    if entry.canCuff then label=label..' · нужны наручники'end
    mark(entry.actor,'body',label,entry.actor.Mesh)
   end
  end
 end
 if options.traps then
  for _,entry in ipairs(doorActors)do
   local door=entry.actor
   if valid(door)then
    if not entry.stateAt or now>=entry.stateAt then
     local ok,live=pcall(function()return door:IsTrapLive()end)
     entry.live=ok and live==true;entry.trap=nil
     if entry.live then local ok,trap=pcall(function()return door:GetAttachedTrap()end);if ok and valid(trap)then entry.trap=trap end end
     entry.stateAt=now+options.healthMs/1000
    end
    if entry.live then
     -- Door state is replicated even when the attached trap isn't available locally.
     local target=valid(entry.trap)and entry.trap or door
     pcall(mark,target,'trap','Активна')
    end
   end
  end
 end
 for _,entry in ipairs(evidenceActors)do local a,c=entry.actor,entry.component
  if valid(a)and valid(c)and c:CanBeCollected()and not c:IsEvidenceCollected()and not c.bEvidenceExtracted then evidence=evidence+1;if options.evidence then mark(a,'evidence',entry.name)end end
 end
 for _,a in ipairs(reportActors)do
  if valid(a)and belongs(a,world)and a.bReportableEnabled and not a.bHasBeenReported then reports=reports+1;if options.reports then mark(a,'report',name(a,'ReportableName','Объект для доклада'))end end
 end
 for _,a in ipairs(incapacitatedActors)do
  if valid(a)and belongs(a,world)and not a:HasBeenReported()then reports=reports+1;if options.reports then mark(a,'report',a.bIsDead and'Пострадавший: мёртв'or'Пострадавший: ранен',a.HumanMesh)end end
 end
 collectMs=(os.clock()-started)*1000;seq=seq+1
 local header=string.format('F\t%d\t%s\tready\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%.3f\t%.3f\t%.3f\t%s',seq,tostring(lastWorld),width,height,total,civilians,evidence,reports,options.updateMs,collectMs,bonesCostMs,healthCostMs,camera.wire)
 lines[#lines+1]=string.format('V\t%.5f\t%d',projectionError,projectionChecks)
 payload=header..'\n'..table.concat(lines,'\n')..'\nE\n'
end
local function sendLatest()
 if not pipe or not payload then return end
 local value=payload;payload=nil
 local ok=pcall(function()assert(pipe:write(value));assert(pipe:flush())end)
 if not ok then pcall(function()pipe:close()end);pipe=nil end
end
local startLoop
startLoop=function(interval)
 LoopAsync(interval,function()
  local second=os.time()
  if settingsAt~=second then pcall(loadOptions);settingsAt=second end
  if options.updateMs~=interval then startLoop(options.updateMs);return true end
  if not pipe then
   pipe=io.open(pipeName,'wb')
   if pipe then pipe:setvbuf('no');cache={};discoveryAt=-1 end
  end
  if not pipe then
   if (freeCamera or #stationChanges>0)and not pending then pending=true;ExecuteInGameThread(function()pcall(stopFreecam);pcall(restoreGameplay);pending=false end)end
   return false
  end
  if payload then
   local value=payload;payload=nil
   local ok,err=pcall(function()assert(pipe:write(value));assert(pipe:flush())end)
   if not ok then pcall(function()pipe:close()end);pipe=nil;return false end
  end
  if pending then return false end
  pending=true
  ExecuteInGameThread(function()
   local ok,err=pcall(update);pending=false
   ExecuteAsync(sendLatest)
   if not ok then statusFrame('error');print('[RoNESP unified] '..tostring(err)..'\n')end
  end)
  return false
 end)
end
pcall(loadOptions)
print('[RoNESP unified] Split-rate named-pipe telemetry started\n')
startLoop(options.updateMs)
LoopAsync(16,function()
 local data=''
 if options.stationFire and not options.freecam and pipe then
  local f=io.open(stationInputPath,'r');if f then data=f:read('*a');f:close()end
  local stamp=tonumber(data:match('^%x+ (%d+)'))
  if not stamp or os.time()-stamp>1 or os.time()<stamp then data=''end
 end
 if inputPending or data==inputPacket then return false end
 inputPending=true;inputPacket=data
 ExecuteInGameThread(function()
  local ok,err=pcall(applyStationInput,data);inputPending=false
  if not ok then stopStationInput();traceStationInput('error',tostring(err));print('[RoNESP station input] '..tostring(err)..'\n')end
 end)
 return false
end)
