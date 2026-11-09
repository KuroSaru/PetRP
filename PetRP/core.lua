PetRP = PetRP or {}
PetRP.state = {
    petID = nil,
    petName = nil,
    rpName = nil,
    icon = "ability_hunter_beastcall",
    persona = "",
    stopped = false,
    periodicCycle = math.random(1, 3), -- 1 = Emote, 2 = Say, 3 = Musician/Sound
    ticker = nil,
    probeTicker = nil,
    hasPendingSmuggle = false,
    pendingSyncCount = 0,
    lastEmoteMsg = nil,
    lastSayMsg = nil,
    previewEmote = nil,
    previewSay = nil,
}

local state = PetRP.state
local SIGNAL_SYNC_PREFIX = "Interface\\AddOns\\PetRP\\sync"

-- 60 Built-in Companion Defaults (30 Physical Emotes + 30 Vocalizations/Messages)
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

local function EnsurePoolsInitialized()
    _G.PetRP_ReactivePool = type(_G.PetRP_ReactivePool) == "table" and _G.PetRP_ReactivePool or {}
    _G.PetRP_EmotePool = type(_G.PetRP_EmotePool) == "table" and _G.PetRP_EmotePool or {}
    _G.PetRP_SayPool = type(_G.PetRP_SayPool) == "table" and _G.PetRP_SayPool or {}

    if type(_G.PetRP_DefaultEmotes) ~= "table" or #_G.PetRP_DefaultEmotes == 0 then
        _G.PetRP_DefaultEmotes = {}
        for i, v in ipairs(BUILTIN_DEFAULT_EMOTES) do _G.PetRP_DefaultEmotes[i] = v end
    end
    if type(_G.PetRP_DefaultSays) ~= "table" or #_G.PetRP_DefaultSays == 0 then
        _G.PetRP_DefaultSays = {}
        for i, v in ipairs(BUILTIN_DEFAULT_SAYS) do _G.PetRP_DefaultSays[i] = v end
    end
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

    -- Roll next random preview so tooltip shows an upcoming random candidate
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
    return #_G.PetRP_ReactivePool, #_G.PetRP_EmotePool, #_G.PetRP_SayPool, #_G.PetRP_DefaultEmotes, #_G.PetRP_DefaultSays
end

function PetRP.PeekNextMessages()
    EnsurePoolsInitialized()
    if not state.previewEmote then
        local emotes = BuildCombinedList(_G.PetRP_EmotePool, _G.PetRP_DefaultEmotes)
        if #emotes > 0 then state.previewEmote = emotes[math.random(1, #emotes)] end
    end
    if not state.previewSay then
        local says = BuildCombinedList(_G.PetRP_SayPool, _G.PetRP_DefaultSays)
        if #says > 0 then state.previewSay = says[math.random(1, #says)] end
    end
    local nextReact = _G.PetRP_ReactivePool[1] or state.previewEmote or ""
    return nextReact, state.previewEmote or "", state.previewSay or ""
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
    EnsurePoolsInitialized()
end

local function CleanText(str)
    if not str then return "" end
    return str:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", ""):gsub("%s+", " "):sub(1, 300)
end

local function ExtractGlances(pe)
    if not pe then return "" end
    local traits = {}
    for i = 1, 5 do
        local slot = pe[tostring(i)]
        if slot and slot.AC and slot.TI then
            traits[#traits + 1] = slot.TI .. (slot.TX and (": " .. slot.TX) or "")
        end
    end
    return table.concat(traits, "; ")
end

local function GetTRP3Persona(lookupName)
    if not (TRP3_API and TRP3_API.companions and TRP3_API.companions.player) then return lookupName, "", nil end
    local profile = TRP3_API.companions.player.getCompanionProfile(lookupName)
    if not (profile and profile.data) then return lookupName, "", nil end
    local d = profile.data
    local rpName = d.NA or lookupName
    local iconInfo = TRP3_IconUtil and TRP3_IconUtil.GetIconInfo and TRP3_IconUtil.GetIconInfo(d.IC)
    local iconName = (TRP3_IconUtil and TRP3_IconUtil.GetIconName and TRP3_IconUtil.GetIconName(d.IC))
        or (iconInfo and (iconInfo.file or iconInfo.atlas)) or d.IC
    if PetRP_Config and iconName then
        PetRP_Config.iconNames = PetRP_Config.iconNames or {}
        PetRP_Config.iconNames[lookupName] = tostring(iconName):lower()
        if d.IC then PetRP_Config.iconNames[tostring(d.IC)] = tostring(iconName):lower() end
    end
    local details = string.format("Name: %s | Title: %s | At First Glance: %s | About: %s",
        rpName, CleanText(d.TI), CleanText(ExtractGlances(profile.PE)), CleanText(d.TX))
    return rpName, details, d.IC
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
        return "pet:" .. unitPet, unitPet, GetPetIcon and GetPetIcon() or "ability_hunter_beastcall"
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

function PetRP.DeliverCompanionChat(msg, force, mode)
    if not msg or msg == "" then return false end
    if not force and (state.stopped or not state.petName) then return false end
    local speaker = state.rpName or state.petName or "Companion"
    local cleanMsg = msg:gsub("^||%s*", ""):gsub("^|%s*", ""):gsub('^"', ''):gsub('"$', '')
    local prefix = (TRP3_API and TRP3_API.configuration and TRP3_API.configuration.getValue
        and TRP3_API.configuration.getValue("npc_talk_prefix")) or "|| "

    if speaker ~= "" and cleanMsg:lower():sub(1, #speaker + 1) == (speaker:lower() .. " ") then
        cleanMsg = cleanMsg:sub(#speaker + 2):gsub("^%s+", "")
    end

    if mode == "SAY" then
        if cleanMsg:match("^%*.*%*$") then
            local act = cleanMsg:gsub("^%*", ""):gsub("%*$", ""):gsub("^%s+", ""):gsub("%s+$", "")
            if speaker ~= "" and act:lower():sub(1, #speaker + 1) == (speaker:lower() .. " ") then
                act = act:sub(#speaker + 2):gsub("^%s+", "")
            end
            SendChatMessage(string.format("%s[%s] %s", prefix, speaker, act), "EMOTE")
        else
            SendChatMessage(string.format('%s[%s] says, "%s"', prefix, speaker, cleanMsg), "EMOTE")
        end
    elseif mode == "SOUND" then
        PetRP.PlayCompanionSound()
        SendChatMessage(string.format("%s[%s] %s", prefix, speaker, cleanMsg), "EMOTE")
    else
        SendChatMessage(string.format("%s[%s] %s", prefix, speaker, cleanMsg), "EMOTE")
    end
    return true
end

local function CycleNextPeriodic(force)
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
    EnsurePoolsInitialized()
    if #_G.PetRP_ReactivePool > 0 then
        local idx = math.random(1, #_G.PetRP_ReactivePool)
        local reactMsg = table.remove(_G.PetRP_ReactivePool, idx)
        if PetRP.DeliverCompanionChat(reactMsg, true, "EMOTE") then
            if PetRP.UpdateUI then PetRP.UpdateUI() end
            return
        end
    end

    -- Randomly choose between Emote (1) and Say (2) when clicking the pet icon
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

local function OnPeriodicTick()
    if state.stopped or PetRP_Config.paused or not state.petName then return end
    CycleNextPeriodic(false)
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

function PetRP.RestartTicker()
    if state.ticker then state.ticker:Cancel() end
    state.ticker = C_Timer.NewTicker(PetRP_Config.interval, OnPeriodicTick)
    if not state.probeTicker then
        state.probeTicker = C_Timer.NewTicker(5, PetRP.ProbeSignalFile)
    end
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

function PetRP.SyncCompanionState()
    local id, name, icon = ResolveSummonedCompanion()
    state.petID, state.petName = id, name
    if TRP3_Companions and TRP3_Companions.player and PetRP_Config then
        PetRP_Config.iconNames = PetRP_Config.iconNames or {}
        for _, prof in pairs(TRP3_Companions.player) do
            if prof and prof.data and prof.data.IC then
                local ic = prof.data.IC
                local resolved = (TRP3_IconUtil and TRP3_IconUtil.GetIconName and TRP3_IconUtil.GetIconName(ic)) or ic
                PetRP_Config.iconNames[tostring(ic)] = tostring(resolved):lower()
                for linkName, _ in pairs(prof.links or {}) do
                    PetRP_Config.iconNames[linkName] = tostring(resolved):lower()
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
        state.rpName, state.persona, state.icon = nil, "", "ability_hunter_beastcall"
    else
        local rpName, persona, trpIcon = GetTRP3Persona(name)
        state.rpName, state.persona = rpName, persona
        state.icon = trpIcon or icon or "ability_hunter_beastcall"
    end
    PetRP.ProbeSignalFile()
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

function PetRP.TogglePlayPause()
    if state.stopped then
        state.stopped = false
        PetRP_Config.paused = false
    else
        PetRP_Config.paused = not PetRP_Config.paused
    end
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

function PetRP.Stop()
    state.stopped = true
    PetRP_Config.paused = true
    if PetRP.UpdateUI then PetRP.UpdateUI() end
end

local function IsPetMentioned(msg)
    if state.stopped or not state.petName then return false end
    local lower = msg:lower()
    if lower:find(state.petName:lower(), 1, true) then return true end
    return state.rpName ~= nil and lower:find(state.rpName:lower(), 1, true) ~= nil
end

local function OnEvent(_, event, arg1)
    if event == "ADDON_LOADED" and arg1 == "PetRP" then
        InitConfig()
        if PetRP.InitUI then PetRP.InitUI() end
        PetRP.RestartTicker()
        C_Timer.After(1.0, PetRP.SyncCompanionState)
    elseif event == "PLAYER_ENTERING_WORLD" then
        if LoggingChat then pcall(LoggingChat, true) end
        if PetRP.FlashReloadBeacon then PetRP.FlashReloadBeacon() end
        C_Timer.After(0.5, PetRP.SyncCompanionState)
    elseif event:find("^CHAT_MSG_") then
        if IsPetMentioned(arg1) then
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
        PetRP_Config.interval = math.max(15, tonumber(rest))
        PetRP.RestartTicker()
    else
        PetRP.Speak()
    end
end

SLASH_PETRP1 = "/petrp"
SlashCmdList["PETRP"] = HandleSlash
