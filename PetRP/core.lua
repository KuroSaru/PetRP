local NO_PET_ICON = "Interface\\PVPFrame\\Icons\\PVP-Banner-Emblem-40"

PetRP = PetRP or {}
PetRP.NO_PET_ICON = NO_PET_ICON
PetRP.state = {
    petID = nil,
    petName = nil,
    rpName = nil,
    petTitle = "",
    petBody = "",
    petColorHex = "ffd100",
    hasProfile = false,
    loadedPoolPetKey = nil,
    icon = NO_PET_ICON,
    persona = "",
    stopped = false,
    periodicCycle = math.random(1, 3), -- 1 = Emote, 2 = Say, 3 = Musician/Sound
    ticker = nil,
    probeTicker = nil,
    nextTickTime = nil,
    pausedRemaining = nil,
    hasPendingSmuggle = false,
    pendingSyncCount = 0,
    lastReactMsg = nil,
    lastEmoteMsg = nil,
    lastSayMsg = nil,
    previewReact = nil,
    previewEmote = nil,
    previewSay = nil,
    lastSoundSig = nil,
}

local state = PetRP.state
local SIGNAL_SYNC_PREFIX = "Interface\\AddOns\\PetRP\\sync"

-- 90 Built-in Companion Defaults (30 Reactive Emotes + 30 Periodic Emotes + 30 Periodic Vocalizations/Says)
local BUILTIN_DEFAULT_REACTIVE = {
    "perks up immediately at the mention of its name, tilting its head with bright, attentive eyes.",
    "turns its gaze sharply toward the speaker, wiggling in recognition.",
    "bounds a step closer when spoken to, radiating eager curiosity.",
    "swivels its attention toward the voice and gives an affectionate little nod.",
    "looks back and forth between the speaker and its master as if following every word.",
    "bounces on its heels with unmistakable delight at being noticed.",
    "sidles right up to its master's side, puffing up protectively at the mention of their name.",
    "cocks its head inquisitively at the speaker, waiting to see what happens next.",
    "gives an enthusiastic little hop to acknowledge that it is listening closely.",
    "fixes an unblinking, expressive gaze upon whoever just spoke.",
    "nudges its master's leg in quiet solidarity after hearing their name.",
    "fluffs up with sudden pride upon hearing itself mentioned in conversation.",
    "trots a small half-circle in place, clearly pleased to be included.",
    "leans forward attentively, ears and eyes locked onto the speaker.",
    "lets out a soft, reactive little huff and shifts into an alert stance.",
    "blinks twice at the speaker before offering a cheerful little bob of its head.",
    "watches the speaker with narrows eyes full of playful mischief.",
    "steps out slightly in front of its master, acting like a tiny bodyguard.",
    "tilts its head from left to right, thoroughly intrigued by the remark.",
    "gives a quick, theatrical spin in response to hearing its name called.",
    "peers up at its master for approval before looking back at the speaker.",
    "patters its feet rapidly on the ground, excited by the sudden attention.",
    "draws itself up to its full miniature height as if answering roll call.",
    "sways happily in place, delighted to be part of the discussion.",
    "glances sharply toward the speaker, tracking every gesture with keen interest.",
    "presses affectionately against its master's shin as the conversation turns their way.",
    "offers a spirited little salute of a hop when addressed.",
    "watches the speaker with wide, gleaming eyes full of anticipation.",
    "pivots smoothly toward the voice, poised and ready for action.",
    "beams with pint-sized importance the moment its name echoes nearby."
}

local BUILTIN_DEFAULT_EMOTES = {
    "tilts its head curiously and watches everyone nearby.",
    "perks up suddenly, scanning the area with bright eyes.",
    "circles around its master's heels before settling down.",
    "stretches lazily from head to toe and gives a content shake.",
    "sniffs at the air inquisitively, catching an unfamiliar scent.",
    "chases a drifting speck of dust in a tight little circle.",
    "nudges gently against its master's leg for attention.",
    "peers intently at a passing adventurer with undisguised curiosity.",
    "bounces in place with a sudden burst of playful energy.",
    "settles comfortably and observes the surroundings with quiet vigilance.",
    "cocks its head to one side as if listening to a distant whisper.",
    "pats softly at the ground while keeping watch over the group.",
    "fluffs up proudly and struts a few paces forward.",
    "blinks slowly, looking thoroughly pleased with itself.",
    "darts a quick glance toward the horizon before returning to its master's side.",
    "sways gently in rhythm with the ambient sounds of the world.",
    "paws playfully at a loose pebble on the ground.",
    "stands tall and alert, mimicking a miniature guardian.",
    "leans in close to inspect a nearby shadow before hopping back.",
    "gives a spirited little hop, eager to keep moving.",
    "curls up briefly, then pops right back up as if refusing to miss anything.",
    "watches the conversation intently, shifting its gaze from speaker to speaker.",
    "lets out a tiny puff of breath and wiggles with excitement.",
    "sidles closer to its master, radiating loyal companionship.",
    "traces a small figure-eight pattern around its master's boots.",
    "twitches attentively at every sudden sound in the distance.",
    "performs a quick, theatrical spin before freezing in a proud pose.",
    "rubs its cheek affectionately against its master's knee.",
    "peers up at its master as though waiting for a secret signal.",
    "keeps a watchful, mischievous gaze fixed on the nearest shiny object."
}

local BUILTIN_DEFAULT_SAYS = {
    "*lets out a soft, inquisitive chirp.*",
    "*makes a cheerful little trill of approval.*",
    "*murmurs softly, eager for the next adventure.*",
    "*chitters quietly as if sharing a secret observation.*",
    "*coos with unmistakable contentment.*",
    "*pipes up with a brief, melodic note.*",
    "*hums a faint, rhythmic little tune under its breath.*",
    "*squeaks excitedly while bouncing on its heels.*",
    "*purrs with a low, soothing resonance.*",
    "*yips softly to remind everyone it is still supervising.*",
    "*makes an amused little snort at the ongoing conversation.*",
    "*whistles a short, curious two-tone call.*",
    "*clicks and chirps in animated agreement.*",
    "*lets out a dramatic little sigh, demanding attention.*",
    "*burbles happily, completely in its element.*",
    "*chatters softly at the surrounding scenery.*",
    "*gives a bright, encouraging peep.*",
    "*rumbles with quiet, pint-sized determination.*",
    "*trills a playful greeting to those nearby.*",
    "*mutters a tiny, mischievous sound.*",
    "*warbles a sweet, lingering note.*",
    "*lets out an expectant little hum, watching its master closely.*",
    "*chirrups with sudden enthusiasm!*",
    "*makes a sleepy, contented little murmur.*",
    "*offers a crisp, attentive vocalization.*",
    "*chuckles in its own tiny, unmistakable way.*",
    "*whispers a faint, echoing sound into the breeze.*",
    "*peeps inquisitively, tilting its head for an answer.*",
    "*buzzes with barely contained excitement.*",
    "*croons a gentle, reassuring note from beside its master.*"
}

local function CopyList(src)
    local out = {}
    if type(src) == "table" then
        for i, v in ipairs(src) do out[i] = v end
    end
    return out
end

local function UnloadAllPools()
    _G.PetRP_ReactivePool = {}
    _G.PetRP_EmotePool = {}
    _G.PetRP_SayPool = {}
    _G.PetRP_DefaultReactive = {}
    _G.PetRP_DefaultEmotes = {}
    _G.PetRP_DefaultSays = {}
    state.loadedPoolPetKey = nil
    state.lastReactMsg = nil
    state.lastEmoteMsg = nil
    state.lastSayMsg = nil
    state.previewReact = nil
    state.previewEmote = nil
    state.previewSay = nil
end

local function FindPetPoolEntry(petName, rpName)
    if type(_G.PetRP_PetPools) ~= "table" then return nil end
    local candidates = {}
    if rpName and rpName ~= "" then
        candidates[#candidates + 1] = rpName
        candidates[#candidates + 1] = rpName:lower()
    end
    if petName and petName ~= "" then
        candidates[#candidates + 1] = petName
        candidates[#candidates + 1] = petName:lower()
    end
    for _, key in ipairs(candidates) do
        if type(_G.PetRP_PetPools[key]) == "table" then
            return _G.PetRP_PetPools[key]
        end
    end
    for k, v in pairs(_G.PetRP_PetPools) do
        if type(k) == "string" and type(v) == "table" then
            local kl = k:lower()
            if (rpName and kl == rpName:lower()) or (petName and kl == petName:lower()) then
                return v
            end
        end
    end
    return nil
end

local function SyncActivePetPools()
    if not state.petName or not state.hasProfile then
        UnloadAllPools()
        return
    end
    local petKey = ((state.rpName or state.petName) or ""):lower()
    if state.loadedPoolPetKey == petKey and type(_G.PetRP_DefaultEmotes) == "table" and #_G.PetRP_DefaultEmotes > 0 then
        return
    end

    local entry = FindPetPoolEntry(state.petName, state.rpName)
    if entry then
        _G.PetRP_ReactivePool = CopyList(entry.reactive)
        _G.PetRP_EmotePool = CopyList(entry.emotes)
        _G.PetRP_SayPool = CopyList(entry.says)
        _G.PetRP_DefaultReactive = (type(entry.defReactive) == "table" and #entry.defReactive > 0) and CopyList(entry.defReactive) or CopyList(BUILTIN_DEFAULT_REACTIVE)
        _G.PetRP_DefaultEmotes = (type(entry.defEmotes) == "table" and #entry.defEmotes > 0) and CopyList(entry.defEmotes) or CopyList(BUILTIN_DEFAULT_EMOTES)
        _G.PetRP_DefaultSays = (type(entry.defSays) == "table" and #entry.defSays > 0) and CopyList(entry.defSays) or CopyList(BUILTIN_DEFAULT_SAYS)
    else
        _G.PetRP_ReactivePool = {}
        _G.PetRP_EmotePool = {}
        _G.PetRP_SayPool = {}
        _G.PetRP_DefaultReactive = CopyList(BUILTIN_DEFAULT_REACTIVE)
        _G.PetRP_DefaultEmotes = CopyList(BUILTIN_DEFAULT_EMOTES)
        _G.PetRP_DefaultSays = CopyList(BUILTIN_DEFAULT_SAYS)
    end
    state.loadedPoolPetKey = petKey
    state.lastReactMsg = nil
    state.lastEmoteMsg = nil
    state.lastSayMsg = nil
    state.previewReact = nil
    state.previewEmote = nil
    state.previewSay = nil
end

local function EnsurePoolsInitialized()
    if not state.petName or not state.hasProfile then
        UnloadAllPools()
        return
    end
    SyncActivePetPools()
end

local function BuildCombinedList(customPool, defaultPool)
    local combined = {}
    local seen = {}
    if type(customPool) == "table" then
        for _, v in ipairs(customPool) do
            if type(v) == "string" and v ~= "" and not seen[v] then
                seen[v] = true
                combined[#combined + 1] = v
            end
        end
    end
    if type(defaultPool) == "table" then
        for _, v in ipairs(defaultPool) do
            if type(v) == "string" and v ~= "" and not seen[v] then
                seen[v] = true
                combined[#combined + 1] = v
            end
        end
    end
    return combined
end

local function PickRandomFromCombined(customPool, defaultPool, lastMsgKey, previewKey)
    EnsurePoolsInitialized()
    local combined = BuildCombinedList(customPool, defaultPool)
    local count = #combined
    if count == 0 then return nil end
    if count == 1 then
        state[lastMsgKey] = combined[1]
        if previewKey then state[previewKey] = combined[1] end
        return combined[1]
    end

    local chosen = state[previewKey]
    if not chosen or chosen == state[lastMsgKey] then
        local idx = math.random(1, count)
        if combined[idx] == state[lastMsgKey] then
            idx = (idx % count) + 1
        end
        chosen = combined[idx]
    end

    state[lastMsgKey] = chosen

    local nextIdx = math.random(1, count)
    if combined[nextIdx] == chosen then
        nextIdx = (nextIdx % count) + 1
    end
    if previewKey then
        state[previewKey] = combined[nextIdx]
    end
    return chosen
end

function PetRP.GetPoolCounts()
    EnsurePoolsInitialized()
    return #_G.PetRP_ReactivePool, #_G.PetRP_EmotePool, #_G.PetRP_SayPool, #_G.PetRP_DefaultEmotes, #_G.PetRP_DefaultSays, #(_G.PetRP_DefaultReactive or {})
end

function PetRP.PeekNextMessages()
    EnsurePoolsInitialized()
    if not state.petName or not state.hasProfile then
        return "", "", ""
    end
    if not state.previewReact then
        local reacts = BuildCombinedList(_G.PetRP_ReactivePool, _G.PetRP_DefaultReactive)
        if #reacts > 0 then state.previewReact = reacts[math.random(1, #reacts)] end
    end
    if not state.previewEmote then
        local emotes = BuildCombinedList(_G.PetRP_EmotePool, _G.PetRP_DefaultEmotes)
        if #emotes > 0 then state.previewEmote = emotes[math.random(1, #emotes)] end
    end
    if not state.previewSay then
        local says = BuildCombinedList(_G.PetRP_SayPool, _G.PetRP_DefaultSays)
        if #says > 0 then state.previewSay = says[math.random(1, #says)] end
    end
    return state.previewReact or "", state.previewEmote or "", state.previewSay or ""
end

function PetRP.ComputePetBeaconByte(nameStr)
    if not nameStr or nameStr == "" then return 0 end
    local s = nameStr:lower()
    local sum = 0
    for i = 1, #s do
        sum = (sum * 31 + s:byte(i)) % 231
    end
    return 20 + sum
end

function PetRP.OpenTRP3CompanionsDialog()
    if TRP3_API and TRP3_API.navigation then
        if TRP3_API.navigation.openMainFrame then
            pcall(TRP3_API.navigation.openMainFrame)
        end
        if TRP3_API.navigation.menu and TRP3_API.navigation.menu.selectMenu then
            local ok = pcall(TRP3_API.navigation.menu.selectMenu, "main_20_companions_20_profiles")
            if not ok then
                pcall(TRP3_API.navigation.menu.selectMenu, "main_20_companions")
            end
        end
        return
    end
    if SlashCmdList and SlashCmdList["TOTALRP3"] then
        pcall(SlashCmdList["TOTALRP3"], "")
    end
end

-- Sequence-numbered signal probe: checks ONLY sync<PetRP_SyncSeq + 1>.mp3 (created on disk only when an update is pending)
function PetRP.ProbeSignalFile()
    if not PlaySoundFile then return end
    local loadedSeq = tonumber(_G.PetRP_SyncSeq) or 100
    local nextSlot = loadedSeq + 1
    local path = SIGNAL_SYNC_PREFIX .. tostring(nextSlot) .. ".mp3"

    local ok, willPlay, handle = pcall(PlaySoundFile, path, "SFX")
    if ok and handle and StopSound then
        pcall(StopSound, handle)
    end

    local ready = (ok and willPlay == true)
    if ready ~= state.hasPendingSmuggle then
        state.hasPendingSmuggle = ready
        state.pendingSyncCount = ready and 1 or 0
        if PetRP.UpdateUI then PetRP.UpdateUI() end
    end
end

local function InitConfig()
    PetRP_Config = PetRP_Config or {}
    if PetRP_Config.paused == nil then PetRP_Config.paused = false end
    PetRP_Config.interval = PetRP_Config.interval or 180
    PetRP_Config.scale = PetRP_Config.scale or 1.0
    PetRP_Config.pos = PetRP_Config.pos or { point = "CENTER", x = 0, y = -140 }
    PetRP_Config.seenPets = PetRP_Config.seenPets or {}
    PetRP_Config.petSettings = PetRP_Config.petSettings or {}
end

function PetRP.GetPetSettings(petName, rpName)
    InitConfig()
    local pName = petName or state.petName
    local rName = rpName or state.rpName
    local pKey = pName and pName ~= "" and pName:lower() or nil
    local rKey = rName and rName ~= "" and rName:lower() or nil
    if not pKey and not rKey then
        return {
            interval = math.max(15, tonumber(PetRP_Config.interval) or 180),
            autoStart = true,
            speakOnLoad = false,
        }
    end

    local settings = (rKey and PetRP_Config.petSettings[rKey]) or (pKey and PetRP_Config.petSettings[pKey])
    if type(settings) ~= "table" then
        settings = {
            interval = math.max(15, tonumber(PetRP_Config.interval) or 180),
            autoStart = true,
            speakOnLoad = false,
        }
    else
        settings.interval = math.max(15, tonumber(settings.interval) or tonumber(PetRP_Config.interval) or 180)
        if settings.autoStart == nil then settings.autoStart = true end
        if settings.speakOnLoad == nil then settings.speakOnLoad = false end
    end

    if pKey then PetRP_Config.petSettings[pKey] = settings end
    if rKey then PetRP_Config.petSettings[rKey] = settings end
    return settings
end

function PetRP.GetActiveInterval()
    local cfg = PetRP.GetPetSettings()
    return math.max(15, tonumber(cfg and cfg.interval) or tonumber(PetRP_Config and PetRP_Config.interval) or 180)
end

function PetRP.GetPetColorRGB()
    local hex = (state.hasProfile and type(state.petColorHex) == "string" and state.petColorHex:match("^%x%x%x%x%x%x$"))
        and state.petColorHex:lower() or "ffd100"
    local r = tonumber(hex:sub(1, 2), 16) / 255
    local g = tonumber(hex:sub(3, 4), 16) / 255
    local b = tonumber(hex:sub(5, 6), 16) / 255
    return r, g, b, hex
end

function PetRP.GetRemainingPeriodicSeconds()
    local interval = PetRP.GetActiveInterval()
    if state.stopped then
        return 0, "Stopped"
    elseif PetRP_Config and PetRP_Config.paused then
        return math.max(1, tonumber(state.remainingSeconds) or interval), "Paused"
    end
    local rem = math.ceil(tonumber(state.remainingSeconds) or interval)
    if rem < 0 or rem > interval then rem = interval end
    return rem, "Active"
end

local function CleanText(str)
    if not str then return "" end
    return str:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", ""):gsub("%s+", " "):gsub("^%s+", ""):gsub("%s+$", ""):sub(1, 300)
end

local function CleanBodyText(str)
    if not str then return "" end
    return str
        :gsub("%{img:[^}]+%}", "")
        :gsub("%{icon:[^}]+%}", "")
        :gsub("%{link%*[^}]+%}", "")
        :gsub("%{/?h%d?[^}]*%}", "")
        :gsub("%{/?p[^}]*%}", "")
        :gsub("%{col:[^}]+%}", "")
        :gsub("%{/col%}", "")
        :gsub("|c%x%x%x%x%x%x%x%x", "")
        :gsub("|r", "")
        :gsub("%s+", " ")
        :gsub("^%s+", "")
        :gsub("%s+$", "")
        :sub(1, 350)
end

local function GetTRP3Persona(lookupName)
    if not lookupName or lookupName == "" then return nil, "", "", "ffd100", nil, false end
    local profile = nil
    if TRP3_API and TRP3_API.companions and TRP3_API.companions.player and TRP3_API.companions.player.getCompanionProfile then
        profile = TRP3_API.companions.player.getCompanionProfile(lookupName)
    end
    if not (profile and profile.data) and TRP3_Companions and TRP3_Companions.player then
        for _, prof in pairs(TRP3_Companions.player) do
            if prof and prof.links and prof.links[lookupName] and prof.data then
                profile = prof
                break
            end
        end
    end
    if not (profile and profile.data) then
        return nil, "", "", "ffd100", nil, false
    end

    local d = profile.data
    local rpName = (d.NA and d.NA ~= "" and d.NA) or lookupName
    local title = CleanText(d.TI)
    local body = CleanBodyText(d.TX)
    local colorHex = (type(d.NH) == "string" and d.NH:match("^%x%x%x%x%x%x$")) and d.NH:lower() or "ffd100"
    local iconInfo = TRP3_IconUtil and TRP3_IconUtil.GetIconInfo and TRP3_IconUtil.GetIconInfo(d.IC)
    local iconName = (TRP3_IconUtil and TRP3_IconUtil.GetIconName and TRP3_IconUtil.GetIconName(d.IC))
        or (iconInfo and (iconInfo.file or iconInfo.atlas)) or d.IC
    if PetRP_Config and iconName then
        PetRP_Config.iconNames = PetRP_Config.iconNames or {}
        PetRP_Config.iconNames[lookupName] = tostring(iconName):lower()
        if rpName then PetRP_Config.iconNames[rpName] = tostring(iconName):lower() end
        if d.IC then PetRP_Config.iconNames[tostring(d.IC)] = tostring(iconName):lower() end
    end
    return rpName, title, body, colorHex, d.IC, true
end

local function ResolveSummonedCompanion()
    if TRP3_CompanionUtil and TRP3_CompanionUtil.GetSummonedCompanionPetID then
        local id = TRP3_CompanionUtil.GetSummonedCompanionPetID()
        local info = id and TRP3_CompanionUtil.GetCompanionPetInfo(id)
        if info and info.name then return id, info.name, info.icon end
    end
    if C_PetJournal and C_PetJournal.GetSummonedPetGUID then
        local guid = C_PetJournal.GetSummonedPetGUID()
        if guid then
            local _, custom, _, _, _, _, _, species, icon = C_PetJournal.GetPetInfoByPetID(guid)
            return guid, (custom ~= "" and custom) or species, icon
        end
    end
    local unitPet = UnitExists("pet") and UnitNameUnmodified("pet")
    if unitPet and unitPet ~= UNKNOWNOBJECT then
        return "pet:" .. unitPet, unitPet, GetPetIcon and GetPetIcon() or NO_PET_ICON
    end
    return nil, nil, nil
end

function PetRP.PlayCompanionSound()
    local played = false
    if Musician and Musician.Sampler then
        local notes = { 60, 64, 67, 72, 69 }
        local n1, n2 = notes[math.random(#notes)], notes[math.random(#notes)]
        local inst = (Musician.INSTRUMENTS and Musician.INSTRUMENTS.lute and Musician.INSTRUMENTS.lute.midi) or 24
        if Musician.Sampler.PlayNote then
            local ok, handle1 = pcall(Musician.Sampler.PlayNote, inst, n1, false)
            if ok then
                played = true
                C_Timer.After(0.22, function()
                    if handle1 and Musician.Sampler.StopNote then pcall(Musician.Sampler.StopNote, handle1) end
                    local _, handle2 = pcall(Musician.Sampler.PlayNote, inst, n2, false)
                    C_Timer.After(0.35, function()
                        if handle2 and Musician.Sampler.StopNote then pcall(Musician.Sampler.StopNote, handle2) end
                    end)
                end)
            end
        end
    end
    if not played and PlaySound and SOUNDKIT then
        PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON or 856, "SFX")
    end
end

local function FormatIconMarkup(iconVal)
    if not iconVal or iconVal == "" then return "" end
    local texPath = iconVal
    if type(iconVal) == "string" and not iconVal:find("\\") and not tonumber(iconVal) then
        texPath = "Interface\\Icons\\" .. iconVal
    end
    return string.format("|T%s:16:16:0:0:64:64:4:60:4:60|t ", tostring(texPath))
end

local function RenderLocalCompanionFallback(speaker, bodyText, isSpokenSay)
    if not DEFAULT_CHAT_FRAME or not DEFAULT_CHAT_FRAME.AddMessage then return end
    local _, _, _, pHex = PetRP.GetPetColorRGB()
    local iconTag = FormatIconMarkup(state.icon)
    local info = ChatTypeInfo and ChatTypeInfo["EMOTE"]
    local r = (info and info.r) or 1.0
    local g = (info and info.g) or 0.5
    local b = (info and info.b) or 0.25
    local formatted
    if isSpokenSay then
        formatted = string.format('%s|cff%s%s|r says, "%s"', iconTag, pHex or "ffd100", speaker, bodyText)
    else
        formatted = string.format("%s|cff%s%s|r %s", iconTag, pHex or "ffd100", speaker, bodyText)
    end
    DEFAULT_CHAT_FRAME:AddMessage(formatted, r, g, b)
end

function PetRP.DeliverCompanionChat(msg, force, mode)
    if not msg or msg == "" then return false end
    if not state.petName or not state.hasProfile then return false end
    if not force and state.stopped then return false end
    local speaker = state.rpName or state.petName or "Companion"
    local cleanMsg = msg:gsub("^||%s*", ""):gsub("^|%s*", ""):gsub('^"', ''):gsub('"$', '')
    local prefix = (TRP3_API and TRP3_API.configuration and TRP3_API.configuration.getValue
        and TRP3_API.configuration.getValue("npc_talk_prefix")) or "|| "

    if speaker ~= "" and cleanMsg:lower():sub(1, #speaker + 1) == (speaker:lower() .. " ") then
        cleanMsg = cleanMsg:sub(#speaker + 2):gsub("^%s+", "")
    end

    local chatLine
    local fallbackBody = cleanMsg
    local isSpokenSay = false

    if mode == "SAY" then
        if cleanMsg:match("^%*.*%*$") then
            local act = cleanMsg:gsub("^%*", ""):gsub("%*$", ""):gsub("^%s+", ""):gsub("%s+$", "")
            if speaker ~= "" and act:lower():sub(1, #speaker + 1) == (speaker:lower() .. " ") then
                act = act:sub(#speaker + 2):gsub("^%s+", "")
            end
            fallbackBody = act
            chatLine = string.format("%s[%s] %s", prefix, speaker, act)
        else
            isSpokenSay = true
            chatLine = string.format('%s[%s] says, "%s"', prefix, speaker, cleanMsg)
        end
    elseif mode == "SOUND" then
        PetRP.PlayCompanionSound()
        chatLine = string.format("%s[%s] %s", prefix, speaker, cleanMsg)
    else
        chatLine = string.format("%s[%s] %s", prefix, speaker, cleanMsg)
    end

    local sendAt = GetTime and GetTime() or 0
    pcall(SendChatMessage, chatLine, "EMOTE")
    C_Timer.After(0.35, function()
        if (state.lastEmoteEchoTime or 0) < sendAt then
            RenderLocalCompanionFallback(speaker, fallbackBody, isSpokenSay)
        end
    end)
    return true
end

local function CycleNextPeriodic(force)
    if not state.petName or not state.hasProfile then return false end
    EnsurePoolsInitialized()
    local order = { state.periodicCycle, (state.periodicCycle % 3) + 1, ((state.periodicCycle + 1) % 3) + 1 }
    for _, step in ipairs(order) do
        if step == 1 then
            local emoteMsg = PickRandomFromCombined(_G.PetRP_EmotePool, _G.PetRP_DefaultEmotes, "lastEmoteMsg", "previewEmote")
            if emoteMsg and PetRP.DeliverCompanionChat(emoteMsg, force, "EMOTE") then
                state.periodicCycle = 2
                return true
            end
        elseif step == 2 then
            local sayMsg = PickRandomFromCombined(_G.PetRP_SayPool, _G.PetRP_DefaultSays, "lastSayMsg", "previewSay")
            if sayMsg and PetRP.DeliverCompanionChat(sayMsg, force, "SAY") then
                state.periodicCycle = 3
                return true
            end
        elseif step == 3 then
            local soundEmote = PickRandomFromCombined(_G.PetRP_EmotePool, _G.PetRP_DefaultEmotes, "lastEmoteMsg", "previewEmote") or "makes a soft melodic sound."
            if PetRP.DeliverCompanionChat(soundEmote, force, "SOUND") then
                state.periodicCycle = 1
                return true
            end
        end
    end
    return false
end

function PetRP.Speak()
    PetRP.SyncCompanionState()
    if not state.petName then return end
    if not state.hasProfile then
        PetRP.OpenTRP3CompanionsDialog()
        return
    end
    EnsurePoolsInitialized()
    if #_G.PetRP_ReactivePool > 0 then
        local idx = math.random(1, #_G.PetRP_ReactivePool)
        local reactMsg = table.remove(_G.PetRP_ReactivePool, idx)
        if PetRP.DeliverCompanionChat(reactMsg, true, "EMOTE") then
            if PetRP.UpdateUI then PetRP.UpdateUI() end
            return
        end
    end

    local pickSay = (math.random(1, 2) == 2)
    local delivered = false
    if pickSay then
        local sayMsg = PickRandomFromCombined(_G.PetRP_SayPool, _G.PetRP_DefaultSays, "lastSayMsg", "previewSay")
        if sayMsg then
            delivered = PetRP.DeliverCompanionChat(sayMsg, true, "SAY")
        end
    end
    if not delivered then
        local emoteMsg = PickRandomFromCombined(_G.PetRP_EmotePool, _G.PetRP_DefaultEmotes, "lastEmoteMsg", "previewEmote")
        if emoteMsg then
            delivered = PetRP.DeliverCompanionChat(emoteMsg, true, "EMOTE")
        end
    end
    if not delivered then
        PetRP.PlayCompanionSound()
    end
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

local function OnHeartbeatTick()
    if state.stopped or (PetRP_Config and PetRP_Config.paused) or not state.petName or not state.hasProfile then
        return
    end
    local interval = PetRP.GetActiveInterval()
    local rem = (tonumber(state.remainingSeconds) or interval) - 1
    if rem <= 0 then
        state.remainingSeconds = interval
        CycleNextPeriodic(false)
        if PetRP.UpdateUI then PetRP.UpdateUI() end
    else
        state.remainingSeconds = rem
    end
end

function PetRP.RestartTicker(keepRemaining)
    local interval = PetRP.GetActiveInterval()
    if not keepRemaining or not state.remainingSeconds or state.remainingSeconds <= 0 or state.remainingSeconds > interval then
        state.remainingSeconds = interval
    end
    if state.ticker then state.ticker:Cancel() end
    state.ticker = C_Timer.NewTicker(1.0, OnHeartbeatTick)
    if not state.probeTicker then
        state.probeTicker = C_Timer.NewTicker(5, PetRP.ProbeSignalFile)
    end
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

local function GetMasterNames()
    local names = {}
    local seen = {}
    local function addName(n)
        if type(n) == "string" then
            local clean = n:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", ""):gsub("^%s+", ""):gsub("%s+$", "")
            if #clean >= 2 then
                local low = clean:lower()
                if not seen[low] then
                    seen[low] = true
                    names[#names + 1] = low
                end
            end
        end
    end

    if UnitName then addName(UnitName("player")) end
    if UnitNameUnmodified then addName(UnitNameUnmodified("player")) end
    if TRP3_API and TRP3_API.profile and TRP3_API.profile.getData then
        local ok, chars = pcall(TRP3_API.profile.getData, "player/characteristics")
        if ok and type(chars) == "table" then
            addName(chars.FN)
            if chars.FN and chars.LN and chars.LN ~= "" then
                addName(chars.FN .. " " .. chars.LN)
            end
        end
    end
    if PetRP_Config then
        PetRP_Config.masterNames = names
    end
    return names
end

function PetRP.SyncCompanionState()
    GetMasterNames()
    local id, name, icon = ResolveSummonedCompanion()
    state.petID, state.petName = id, name
    if TRP3_Companions and TRP3_Companions.player and PetRP_Config then
        PetRP_Config.iconNames = PetRP_Config.iconNames or {}
        PetRP_Config.seenPets = PetRP_Config.seenPets or {}
        for _, prof in pairs(TRP3_Companions.player) do
            if prof and prof.data then
                local ic = prof.data.IC
                local resolved = ic and ((TRP3_IconUtil and TRP3_IconUtil.GetIconName and TRP3_IconUtil.GetIconName(ic)) or ic) or nil
                if ic and resolved then
                    PetRP_Config.iconNames[tostring(ic)] = tostring(resolved):lower()
                end
                for linkName, _ in pairs(prof.links or {}) do
                    if resolved then
                        PetRP_Config.iconNames[linkName] = tostring(resolved):lower()
                    end
                    PetRP_Config.seenPets[linkName] = prof.data.NA or linkName
                end
            end
            if prof and prof.PE then
                for i = 1, 5 do
                    local slot = prof.PE[tostring(i)]
                    if slot and slot.IC then
                        local rSlot = (TRP3_IconUtil and TRP3_IconUtil.GetIconName and TRP3_IconUtil.GetIconName(slot.IC)) or slot.IC
                        PetRP_Config.iconNames[tostring(slot.IC)] = tostring(rSlot):lower()
                    end
                end
            end
        end
    end

    if not name then
        state.rpName, state.petTitle, state.petBody, state.petColorHex = nil, "", "", "ffd100"
        state.persona, state.hasProfile, state.icon = "", false, NO_PET_ICON
        state.lastActivePetKey = nil
        UnloadAllPools()
    else
        if PetRP_Config then
            PetRP_Config.seenPets = PetRP_Config.seenPets or {}
            if icon then
                PetRP_Config.iconNames = PetRP_Config.iconNames or {}
                local rIcon = (TRP3_IconUtil and TRP3_IconUtil.GetIconName and TRP3_IconUtil.GetIconName(icon)) or icon
                PetRP_Config.iconNames[name] = tostring(rIcon):lower()
            end
        end
        local rpName, title, body, colorHex, trpIcon, hasProfile = GetTRP3Persona(name)
        state.hasProfile = (hasProfile == true)
        state.rpName = hasProfile and rpName or nil
        state.petTitle = hasProfile and (title or "") or ""
        state.petBody = hasProfile and (body or "") or ""
        state.petColorHex = hasProfile and (colorHex or "ffd100") or "ffd100"
        state.persona = hasProfile and (body or "") or ""
        state.icon = (hasProfile and trpIcon) or icon or NO_PET_ICON
        if PetRP_Config and PetRP_Config.seenPets then
            PetRP_Config.seenPets[name] = state.rpName or name
        end
        if state.hasProfile then
            SyncActivePetPools()
            local petKey = ((state.rpName or state.petName) or ""):lower()
            local cfg = PetRP.GetPetSettings(state.petName, state.rpName)
            if state.lastActivePetKey ~= petKey then
                state.lastActivePetKey = petKey
                if cfg.autoStart then
                    state.stopped = false
                    PetRP_Config.paused = false
                else
                    state.stopped = true
                    PetRP_Config.paused = true
                end
                PetRP.RestartTicker()
                if cfg.speakOnLoad then
                    C_Timer.After(0.8, function()
                        if state.petName and state.hasProfile and (((state.rpName or state.petName) or ""):lower() == petKey) then
                            PetRP.Speak()
                        end
                    end)
                end
            end
        else
            state.lastActivePetKey = nil
            UnloadAllPools()
        end
    end
    PetRP.ProbeSignalFile()
    if PetRP.UpdateStateBeacon then PetRP.UpdateStateBeacon() end
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

function PetRP.TogglePlayPause()
    if not state.petName or not state.hasProfile then return end
    local interval = PetRP.GetActiveInterval()
    if state.stopped then
        state.stopped = false
        PetRP_Config.paused = false
        state.remainingSeconds = interval
        PetRP.RestartTicker(false)
    elseif PetRP_Config.paused then
        PetRP_Config.paused = false
        if not state.remainingSeconds or state.remainingSeconds <= 0 then
            state.remainingSeconds = interval
        end
        PetRP.RestartTicker(true)
    else
        PetRP_Config.paused = true
    end
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

function PetRP.Stop()
    if not state.petName or not state.hasProfile or state.stopped then return end
    state.stopped = true
    PetRP_Config.paused = true
    state.remainingSeconds = PetRP.GetActiveInterval()
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

local function IsReactiveTrigger(event, msg)
    if state.stopped or not state.petName or not state.hasProfile then return false end
    if type(msg) ~= "string" or msg == "" then return false end
    -- Ignore companion lines generated by PetRP/TRP3 so the pet never triggers itself
    if msg:match("^%s*||%s*%[") or msg:match("^%s*|%s*%[") then return false end

    local lower = msg:lower()
    if lower:find(state.petName:lower(), 1, true) then return true end
    if state.rpName ~= nil and lower:find(state.rpName:lower(), 1, true) then return true end

    -- Also trigger when the pet's master (player) name is said in local (/say) or mentioned by an NPC/monster
    if event == "CHAT_MSG_SAY" or (type(event) == "string" and event:find("^CHAT_MSG_MONSTER_")) then
        for _, mName in ipairs(GetMasterNames()) do
            if lower:find(mName, 1, true) then
                return true
            end
        end
    end
    return false
end

local function OnEvent(_, event, arg1)
    if event == "ADDON_LOADED" and arg1 == "PetRP" then
        InitConfig()
        if PetRP.InitUI then PetRP.InitUI() end
        PetRP.RestartTicker(false)
        C_Timer.After(1.0, PetRP.SyncCompanionState)
    elseif event == "PLAYER_ENTERING_WORLD" then
        if LoggingChat then pcall(LoggingChat, true) end
        if PetRP.FlashReloadBeacon then PetRP.FlashReloadBeacon() end
        C_Timer.After(0.5, PetRP.SyncCompanionState)
    elseif event:find("^CHAT_MSG_") then
        if event == "CHAT_MSG_EMOTE" and type(arg1) == "string" and (arg1:match("^%s*||%s*%[") or arg1:match("^%s*|%s*%[")) then
            state.lastEmoteEchoTime = GetTime and GetTime() or 0
        end
        if IsReactiveTrigger(event, arg1) then
            EnsurePoolsInitialized()
            local reactMsg
            if #_G.PetRP_ReactivePool > 0 then
                local idx = math.random(1, #_G.PetRP_ReactivePool)
                reactMsg = table.remove(_G.PetRP_ReactivePool, idx)
            else
                reactMsg = PickRandomFromCombined(_G.PetRP_EmotePool, _G.PetRP_DefaultEmotes, "lastEmoteMsg", "previewEmote")
            end
            if reactMsg and PetRP.DeliverCompanionChat(reactMsg, false, "EMOTE") then
                if PetRP.UpdateUI then PetRP.UpdateUI() end
            end
        end
    else
        C_Timer.After(0.5, PetRP.SyncCompanionState)
    end
end

local frame = CreateFrame("Frame")
for _, ev in ipairs({
    "ADDON_LOADED", "PLAYER_ENTERING_WORLD", "COMPANION_UPDATE",
    "UNIT_PET", "UNIT_SPELLCAST_SUCCEEDED",
    "CHAT_MSG_SAY", "CHAT_MSG_EMOTE", "CHAT_MSG_TEXT_EMOTE", "CHAT_MSG_YELL",
    "CHAT_MSG_PARTY", "CHAT_MSG_PARTY_LEADER",
    "CHAT_MSG_RAID", "CHAT_MSG_RAID_LEADER", "CHAT_MSG_RAID_WARNING",
    "CHAT_MSG_MONSTER_SAY", "CHAT_MSG_MONSTER_YELL", "CHAT_MSG_MONSTER_EMOTE",
    "CHAT_MSG_MONSTER_WHISPER", "CHAT_MSG_MONSTER_PARTY",
}) do frame:RegisterEvent(ev) end
frame:SetScript("OnEvent", OnEvent)

local function HandleSlash(input)
    local cmd, rest = input:lower():match("^(%S*)%s*(.-)$")
    if cmd == "pause" or cmd == "resume" or cmd == "play" then
        PetRP.TogglePlayPause()
    elseif cmd == "stop" then
        PetRP.Stop()
    elseif cmd == "sound" or cmd == "music" then
        PetRP.PlayCompanionSound()
    elseif cmd == "options" or cmd == "config" then
        if PetRP.ToggleOptions then PetRP.ToggleOptions() end
    elseif cmd == "interval" and tonumber(rest) then
        local newInt = math.max(15, tonumber(rest))
        PetRP_Config.interval = newInt
        local cfg = PetRP.GetPetSettings()
        if cfg then cfg.interval = newInt end
        PetRP.RestartTicker()
    else
        PetRP.Speak()
    end
end

SLASH_PETRP1 = "/petrp"
SlashCmdList["PETRP"] = HandleSlash
