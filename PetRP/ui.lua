local bar, petIcon, statusDot, statusBorder, playPauseBtn, stopBtn, optPanel, syncBtnAbovePet, syncGlowTex, countsLabel
local state = PetRP.state
local NO_PET_ICON = PetRP.NO_PET_ICON or "Interface\\PVPFrame\\Icons\\PVP-Banner-Emblem-40"

-- Matched set from Blizzard's Stopwatch (Blizzard_TimeManager.lua)
local PLAY_ICON = "Interface\\Buttons\\UI-SpellbookIcon-NextPage-Up"
local PAUSE_ICON = "Interface\\TimeManager\\PauseButton"
local STOP_ICON = "Interface\\TimeManager\\ResetButton"
local SYNC_ICON = "Interface\\Buttons\\UI-RefreshButton"
local OPENDYSLEXIC_FONT = "Interface\\AddOns\\PetRP\\fonts\\OpenDyslexic-Regular.ttf"

local function RegisterOpenDyslexicFont()
    if LibStub then
        local ok, lsm = pcall(LibStub, "LibSharedMedia-3.0", true)
        if ok and lsm and lsm.Register then
            pcall(lsm.Register, lsm, "font", "OpenDyslexic", OPENDYSLEXIC_FONT)
        end
    end
end

-- Exact TRP3 Toolbar backdrop (TRP3_BACKDROP_DIALOG_16_16, no header)
local TRP3_STYLE_BACKDROP = {
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true,
    tileEdge = true,
    tileSize = 16,
    edgeSize = 16,
    insets = { left = 5, right = 6, top = 6, bottom = 5 },
}

local function SavePosition()
    local point, _, _, x, y = bar:GetPoint()
    PetRP_Config.pos = { point = point or "CENTER", x = x or 0, y = y or 0 }
end

local function ApplyPetTexture(tex, iconVal)
    if type(iconVal) == "string" and iconVal:find("\\") then
        tex:SetTexture(iconVal)
    elseif TRP3_IconUtil and TRP3_IconUtil.SetTextureToIcon and iconVal and iconVal ~= "" then
        TRP3_IconUtil.SetTextureToIcon(tex, iconVal)
    elseif tonumber(iconVal) then
        tex:SetTexture(tonumber(iconVal))
    elseif type(iconVal) == "string" and iconVal ~= "" then
        tex:SetTexture("Interface\\Icons\\" .. iconVal)
    else
        tex:SetTexture(NO_PET_ICON)
    end
end

local lastTooltipActionLabel = nil

local function RefreshOptionsCountsText()
    if not countsLabel then return end
    if not state.petName then
        countsLabel:SetText("|cffff5555PetRP disabled — no pet loaded.|r\n|cffaaaaaaAll companion messages unloaded.|r")
        return
    elseif not state.hasProfile then
        countsLabel:SetText("|cffffcc00No TRP3 profile for " .. tostring(state.petName) .. ".|r\n|cff00ff99Click pet icon to open TRP3 Companion dialog.|r")
        return
    end
    local nReact, nEmote, nSay, nDefEmote, nDefSay = 0, 0, 0, 0, 0
    if PetRP.GetPoolCounts then
        nReact, nEmote, nSay, nDefEmote, nDefSay = PetRP.GetPoolCounts()
    end
    local totalEmotes = nEmote + nDefEmote
    local totalSays = nSay + nDefSay
    local grandTotal = nReact + totalEmotes + totalSays
    countsLabel:SetText(string.format(
        "|cffffd100Loaded Messages for %s (%d Total):|r\n" ..
        "• Reactive Emotes: |cff00ff00%d Msgs|r\n" ..
        "• Periodic Emotes: |cff00ff00%d Msgs|r\n" ..
        "• Periodic Says: |cff00ff00%d Msgs|r",
        state.rpName or state.petName or "Pet", grandTotal, nReact, totalEmotes, totalSays))
end

local function FormatDurationShort(totalSeconds)
    local s = math.max(0, math.floor(tonumber(totalSeconds) or 0))
    local m = math.floor(s / 60)
    local rem = s % 60
    if m > 0 then
        return string.format("%dm %ds", m, rem)
    end
    return string.format("%ds", rem)
end

local function ShowStatusTooltip(_, actionLabel)
    if not bar then return end
    lastTooltipActionLabel = actionLabel
    GameTooltip:SetOwner(bar, "ANCHOR_TOPLEFT", 0, 6)
    GameTooltip:ClearLines()

    -- Case 1: No pet is loaded -> simple disabled message, no stats
    if not state.petName then
        GameTooltip:AddLine("PetRP disabled — no pet loaded", 1, 0.35, 0.35)
        GameTooltip:Show()
        return
    end

    -- Case 2: Pet is loaded without a TRP3 profile -> prompt to click icon to open TRP3 Companion dialog, no stats
    if not state.hasProfile then
        GameTooltip:AddLine(tostring(state.petName) .. " (No TRP3 Profile)", 1, 0.82, 0)
        GameTooltip:AddLine("Click icon to open TRP3 Companion dialog to make one.", 0.2, 1, 0.6, true)
        GameTooltip:Show()
        return
    end

    -- Case 3: Profiled pet is loaded -> standard TRP3 design colors
    GameTooltip:AddLine("PetRP Companion Bridge", 1, 0.675, 0.125)

    local mode
    if state.stopped then
        mode = "|cffff3333[LED: RED] Stopped|r"
    elseif PetRP_Config.paused then
        mode = "|cffffcc00[LED: YELLOW] Paused|r"
    else
        mode = "|cff00ff00[LED: GREEN] Active|r"
    end

    local nReact, nEmote, nSay, nDefEmote, nDefSay = 0, 0, 0, 30, 30
    if PetRP.GetPoolCounts then
        nReact, nEmote, nSay, nDefEmote, nDefSay = PetRP.GetPoolCounts()
    end
    local nextMsg, perEmote, perSay = "", "", ""
    if PetRP.PeekNextMessages then
        nextMsg, perEmote, perSay = PetRP.PeekNextMessages()
    end

    local totalEmotes = nEmote + nDefEmote
    local totalSays = nSay + nDefSay

    local cfg = (PetRP.GetPetSettings and PetRP.GetPetSettings()) or {}
    local sec = (PetRP.GetActiveInterval and PetRP.GetActiveInterval()) or (PetRP_Config.interval or 180)
    local remSec, remState = sec, "Active"
    if PetRP.GetRemainingPeriodicSeconds then
        remSec, remState = PetRP.GetRemainingPeriodicSeconds()
    end

    local nextTimerSuffix
    if remState == "Stopped" then
        nextTimerSuffix = "|cffff5555(Stopped)|r"
    elseif remState == "Paused" then
        nextTimerSuffix = string.format("|cffffcc00(Paused — %s left)|r", FormatDurationShort(remSec))
    else
        nextTimerSuffix = string.format("|cff00ff99(Next in %s)|r", FormatDurationShort(remSec))
    end
    local intervalText = string.format("%ds (%s) %s", sec, FormatDurationShort(sec), nextTimerSuffix)
    local autoStartText = (cfg.autoStart ~= false) and "|cff00ff00Auto-Play|r" or "|cffff5555Start Stopped|r"
    local speakLoadText = (cfg.speakOnLoad == true) and "|cff00ff00Speak Random|r" or "|cffaaaaaaSilent|r"

    GameTooltip:AddDoubleLine("Status:", mode, 1, 1, 1)
    GameTooltip:AddDoubleLine("Timer:", intervalText, 1, 1, 1)
    GameTooltip:AddDoubleLine("On Pet Load:", autoStartText .. " / " .. speakLoadText, 1, 1, 1)

    GameTooltip:AddDoubleLine("Reactive Emotes:", string.format("|cff00ff00%d Msgs|r", nReact), 1, 1, 1)
    if nextMsg and nextMsg ~= "" then
        GameTooltip:AddLine("  Next: " .. nextMsg, 0.62, 0.62, 0.62, true)
    end

    GameTooltip:AddDoubleLine("Periodic Emotes:", string.format("|cff00ff00%d Msgs|r", totalEmotes), 1, 1, 1)
    if perEmote and perEmote ~= "" then
        GameTooltip:AddLine("  Next: " .. perEmote, 0.62, 0.62, 0.62, true)
    end

    GameTooltip:AddDoubleLine("Periodic Says:", string.format("|cff00ff00%d Msgs|r", totalSays), 1, 1, 1)
    if perSay and perSay ~= "" then
        GameTooltip:AddLine("  Next: " .. perSay, 0.62, 0.62, 0.62, true)
    end

    if state.hasPendingSmuggle then
        GameTooltip:AddDoubleLine("Sync Status:", "|cff00ff99HIGHLIGHTED — Click Sync Icon Above Pet|r", 1, 0.85, 0)
    else
        GameTooltip:AddDoubleLine("Sync Status:", "|cff888888Up to Date|r", 1, 1, 1)
    end

    -- TRP3 Profile: Name, Title, and Body each on their own lines
    GameTooltip:AddLine(" ")
    GameTooltip:AddLine(tostring(state.rpName or state.petName), 1, 0.82, 0)
    if state.petTitle and state.petTitle ~= "" then
        GameTooltip:AddLine("<" .. state.petTitle .. ">", 0.85, 0.85, 0.85, true)
    end
    if state.petBody and state.petBody ~= "" then
        GameTooltip:AddLine(state.petBody, 0.92, 0.92, 0.92, true)
    end

    if actionLabel then
        GameTooltip:AddLine(" ")
        GameTooltip:AddLine(actionLabel, 0.2, 1, 0.6)
    end
    GameTooltip:AddLine("Left-Click Pet: Speak | Right-Click Pet: Options", 0.7, 0.85, 1)
    GameTooltip:AddLine("Drag to move | Bottom-right grip to resize", 0.6, 0.6, 0.6)
    GameTooltip:Show()
end

local function MakeDraggable(widget)
    widget:RegisterForDrag("LeftButton")
    widget:SetScript("OnDragStart", function() bar:StartMoving() end)
    widget:SetScript("OnDragStop", function()
        bar:StopMovingOrSizing()
        SavePosition()
    end)
end

local function CreateControlBtn(parent, xOffset, tex, labelFn, onClick)
    local btn = CreateFrame("Button", nil, parent)
    btn:SetSize(22, 22)
    btn:SetPoint("BOTTOMLEFT", parent, "BOTTOMLEFT", xOffset, 8)
    btn.iconTex = btn:CreateTexture(nil, "ARTWORK")
    btn.iconTex:SetAllPoints()
    btn.iconTex:SetTexture(tex)
    btn:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square", "ADD")
    MakeDraggable(btn)
    btn:SetScript("OnClick", function(self)
        if not state.petName or not state.hasProfile then return end
        onClick()
        ShowStatusTooltip(self, labelFn())
    end)
    btn:SetScript("OnEnter", function(self) ShowStatusTooltip(self, labelFn()) end)
    btn:SetScript("OnLeave", function() GameTooltip:Hide() end)
    return btn
end

local function ShowSyncCountTooltip(btn)
    GameTooltip:SetOwner(btn, "ANCHOR_TOP", 0, 4)
    GameTooltip:ClearLines()
    GameTooltip:AddLine("New messages ready — Click to Reload UI (/reload)", 0.2, 1.0, 0.4)
    GameTooltip:Show()
end

local function CreateSmuggleSyncUI(parent)
    local syncBeaconFrame = CreateFrame("Frame", "PetRPSyncBeaconFrame", UIParent)
    syncBeaconFrame:SetFrameStrata("TOOLTIP")
    syncBeaconFrame:SetFrameLevel(9999)
    syncBeaconFrame:SetSize(2, 2)
    syncBeaconFrame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
    local syncBeacon = syncBeaconFrame:CreateTexture(nil, "OVERLAY")
    syncBeacon:SetAllPoints(syncBeaconFrame)
    syncBeaconFrame:Show()

    local reloadFlashActive = false

    function PetRP.UpdateStateBeacon()
        if reloadFlashActive then
            syncBeacon:SetColorTexture(20 / 255, 247 / 255, 116 / 255, 1.0)
            syncBeaconFrame:Show()
            return
        end
        if not state.petName then
            -- RGB(21, 42, 0) = No Pet Loaded in WoW
            syncBeacon:SetColorTexture(21 / 255, 42 / 255, 0 / 255, 1.0)
        elseif not state.hasProfile then
            -- RGB(21, 42, 10) = Pet Loaded Without TRP3 Profile
            syncBeacon:SetColorTexture(21 / 255, 42 / 255, 10 / 255, 1.0)
        else
            -- RGB(21, 42, 20..250) = Specific Profiled Pet Loaded in WoW
            local bVal = (PetRP.ComputePetBeaconByte and PetRP.ComputePetBeaconByte(state.rpName or state.petName)) or 20
            syncBeacon:SetColorTexture(21 / 255, 42 / 255, bVal / 255, 1.0)
        end
        syncBeaconFrame:Show()
    end

    function PetRP.FlashReloadBeacon()
        reloadFlashActive = true
        PetRP.UpdateStateBeacon()
        C_Timer.After(0.6, function()
            reloadFlashActive = false
            PetRP.UpdateStateBeacon()
        end)
    end

    PetRP.UpdateStateBeacon()

    syncBtnAbovePet = CreateFrame("Button", "PetRPSyncBtn", parent, "BackdropTemplate")
    syncBtnAbovePet:SetSize(28, 28)
    syncBtnAbovePet:SetPoint("BOTTOM", parent, "TOP", 0, 2)
    syncBtnAbovePet:SetBackdrop(TRP3_STYLE_BACKDROP)
    syncBtnAbovePet:Hide()

    local syncTex = syncBtnAbovePet:CreateTexture(nil, "ARTWORK")
    syncTex:SetSize(18, 18)
    syncTex:SetPoint("CENTER", syncBtnAbovePet, "CENTER", 0, 0)
    syncTex:SetTexture(SYNC_ICON)
    syncTex:SetVertexColor(0.3, 1.0, 0.5, 1.0)
    syncBtnAbovePet.iconTex = syncTex

    syncGlowTex = syncBtnAbovePet:CreateTexture(nil, "OVERLAY")
    syncGlowTex:SetSize(26, 26)
    syncGlowTex:SetPoint("CENTER", syncBtnAbovePet, "CENTER", 0, 0)
    syncGlowTex:SetTexture("Interface\\Buttons\\UI-ActionButton-Border")
    syncGlowTex:SetBlendMode("ADD")
    syncGlowTex:SetVertexColor(0.2, 1.0, 0.4, 0.95)

    syncBtnAbovePet:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square", "ADD")

    syncBtnAbovePet:SetScript("OnClick", function()
        if C_UI and C_UI.Reload then
            C_UI.Reload()
        elseif ReloadUI then
            ReloadUI()
        end
    end)
    syncBtnAbovePet:SetScript("OnEnter", function(self)
        ShowSyncCountTooltip(self)
    end)
    syncBtnAbovePet:SetScript("OnLeave", function()
        GameTooltip:Hide()
    end)
end

local function PositionOptionsPanel()
    if not optPanel or not bar then return end
    optPanel:ClearAllPoints()
    local _, barCenterY = bar:GetCenter()
    if barCenterY and (barCenterY * bar:GetEffectiveScale()) < 220 then
        optPanel:SetPoint("BOTTOM", bar, "TOP", 0, 32)
    else
        optPanel:SetPoint("TOP", bar, "BOTTOM", 0, -4)
    end
end

local function CreateOptionsPanel(parent)
    local p = CreateFrame("Frame", "PetRPOptionsPanel", UIParent, "BackdropTemplate")
    p:SetSize(268, 226)
    p:SetFrameStrata("FULLSCREEN_DIALOG")
    p:SetFrameLevel(200)
    p:SetToplevel(true)
    p:SetClampedToScreen(true)
    p:SetBackdrop(TRP3_STYLE_BACKDROP)
    p:Hide()

    local petHeader = p:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    petHeader:SetPoint("TOPLEFT", 12, -10)
    petHeader:SetText("Per-Pet Configuration:")

    local title = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
    title:SetPoint("TOPLEFT", 12, -28)
    title:SetText("Timer Interval (Seconds):")

    local box = CreateFrame("EditBox", nil, p, "InputBoxTemplate")
    box:SetSize(56, 20)
    box:SetPoint("TOPLEFT", 16, -44)
    box:SetAutoFocus(false)
    box:SetNumeric(true)

    local unitLbl = p:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    unitLbl:SetPoint("LEFT", box, "RIGHT", 6, 0)
    local function UpdateUnitLabel(val)
        local s = math.max(15, tonumber(val) or 180)
        unitLbl:SetText(string.format("sec (%dm %ds)", math.floor(s / 60), s % 60))
    end

    local function CommitIntervalFromBox(forceRestart)
        local newInt = math.max(15, tonumber(box:GetText()) or 180)
        local cfg = (PetRP.GetPetSettings and PetRP.GetPetSettings()) or nil
        local prevInt = (cfg and tonumber(cfg.interval)) or tonumber(PetRP_Config.interval) or 180
        PetRP_Config.interval = newInt
        if cfg then cfg.interval = newInt end
        box:SetText(tostring(newInt))
        UpdateUnitLabel(newInt)
        if forceRestart or newInt ~= prevInt then
            PetRP.RestartTicker(false)
        end
    end

    box:SetScript("OnTextChanged", function(self)
        UpdateUnitLabel(self:GetText())
    end)
    box:SetScript("OnEnterPressed", function(self)
        CommitIntervalFromBox(true)
        self:ClearFocus()
    end)
    box:SetScript("OnEditFocusLost", function()
        CommitIntervalFromBox(false)
    end)

    local autoStartChk = CreateFrame("CheckButton", nil, p, "UICheckButtonTemplate")
    autoStartChk:SetSize(22, 22)
    autoStartChk:SetPoint("TOPLEFT", 10, -68)
    local autoStartLbl = p:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    autoStartLbl:SetPoint("LEFT", autoStartChk, "RIGHT", 2, 0)
    autoStartLbl:SetText("Auto-Start Timer When Pet Loads")
    autoStartChk:SetScript("OnClick", function(self)
        local checked = self:GetChecked() and true or false
        if PetRP.GetPetSettings then
            local cfg = PetRP.GetPetSettings()
            if cfg then cfg.autoStart = checked end
        end
    end)

    local speakLoadChk = CreateFrame("CheckButton", nil, p, "UICheckButtonTemplate")
    speakLoadChk:SetSize(22, 22)
    speakLoadChk:SetPoint("TOPLEFT", 10, -90)
    local speakLoadLbl = p:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    speakLoadLbl:SetPoint("LEFT", speakLoadChk, "RIGHT", 2, 0)
    speakLoadLbl:SetText("Speak Random Message on Load (vs Silent)")
    speakLoadChk:SetScript("OnClick", function(self)
        local checked = self:GetChecked() and true or false
        if PetRP.GetPetSettings then
            local cfg = PetRP.GetPetSettings()
            if cfg then cfg.speakOnLoad = checked end
        end
    end)

    countsLabel = p:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    countsLabel:SetPoint("TOPLEFT", 12, -118)
    countsLabel:SetJustifyH("LEFT")
    countsLabel:SetWidth(244)

    local resetBtn = CreateFrame("Button", nil, p, "UIPanelButtonTemplate")
    resetBtn:SetSize(112, 22)
    resetBtn:SetPoint("BOTTOMLEFT", 12, 12)
    resetBtn:SetText("Reset Size")
    resetBtn:SetScript("OnClick", function()
        PetRP_Config.scale = 1.0
        parent:SetScale(1.0)
        p:SetScale(1.0)
    end)

    local syncBtn = CreateFrame("Button", nil, p, "UIPanelButtonTemplate")
    syncBtn:SetSize(112, 22)
    syncBtn:SetPoint("BOTTOMRIGHT", -12, 12)
    syncBtn:SetText("Sync Pet")
    syncBtn:SetScript("OnClick", function()
        PetRP.SyncCompanionState()
        if p.RefreshControls then p:RefreshControls() end
    end)

    function p:RefreshControls()
        local petLabel = state.rpName or state.petName or "None"
        petHeader:SetText("|cffffd100Pet Settings: " .. tostring(petLabel) .. "|r")
        local cfg = (PetRP.GetPetSettings and PetRP.GetPetSettings()) or {}
        local cur = tonumber(cfg.interval) or tonumber(PetRP_Config.interval) or 180
        if not box:HasFocus() then
            box:SetText(tostring(cur))
        end
        UpdateUnitLabel(cur)
        autoStartChk:SetChecked(cfg.autoStart ~= false)
        speakLoadChk:SetChecked(cfg.speakOnLoad == true)
        RefreshOptionsCountsText()
    end

    p:SetScript("OnShow", function(self)
        self:SetScale(parent:GetScale())
        PositionOptionsPanel()
        self:RefreshControls()
    end)
    p:SetScript("OnHide", function()
        CommitIntervalFromBox(false)
    end)
    return p
end

local function AttachDragAndResize(frame)
    frame:SetMovable(true)
    frame:SetClampedToScreen(true)
    frame:EnableMouse(true)
    MakeDraggable(frame)

    local grip = CreateFrame("Button", nil, frame)
    grip:SetSize(14, 14)
    grip:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -2, 2)
    grip:SetFrameLevel(frame:GetFrameLevel() + 10)
    grip:SetNormalTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Up")
    grip:SetHighlightTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Highlight")
    grip:SetPushedTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Down")
    grip:SetScript("OnMouseDown", function(self)
        self.startY = select(2, GetCursorPosition())
        self.startScale = frame:GetScale()
        self:SetScript("OnUpdate", function(s)
            local curY = select(2, GetCursorPosition())
            local newScale = math.max(0.6, math.min(2.5, s.startScale + (s.startY - curY) / 150))
            frame:SetScale(newScale)
            if optPanel and optPanel:IsShown() then
                optPanel:SetScale(newScale)
                PositionOptionsPanel()
            end
            PetRP_Config.scale = newScale
        end)
    end)
    grip:SetScript("OnMouseUp", function(self) self:SetScript("OnUpdate", nil) end)
end

function PetRP.ToggleOptions()
    if not optPanel then return end
    PositionOptionsPanel()
    optPanel:SetShown(not optPanel:IsShown())
end

function PetRP.UpdateUI()
    if not bar then return end
    ApplyPetTexture(petIcon, state.icon)
    local controlsActive = (state.petName ~= nil) and (state.hasProfile == true)
    petIcon:SetDesaturated(controlsActive and state.stopped or (state.petName ~= nil and not state.hasProfile))

    local isPausedOrStopped = state.stopped or PetRP_Config.paused or not controlsActive
    if playPauseBtn then
        playPauseBtn:SetEnabled(controlsActive)
        playPauseBtn:SetAlpha(controlsActive and 1.0 or 0.35)
        if playPauseBtn.iconTex then
            playPauseBtn.iconTex:SetTexture(isPausedOrStopped and PLAY_ICON or PAUSE_ICON)
            playPauseBtn.iconTex:SetDesaturated(not controlsActive)
        end
    end

    -- Stop button is ONLY clickable if a profiled pet is loaded AND in Play mode or Paused mode (not already Stopped)
    local canStop = controlsActive and (not state.stopped)
    if stopBtn then
        stopBtn:SetEnabled(canStop)
        stopBtn:SetAlpha(canStop and 1.0 or 0.35)
        if stopBtn.iconTex then
            stopBtn.iconTex:SetDesaturated(not canStop)
        end
    end

    if syncBtnAbovePet then
        local showSync = (state.hasPendingSmuggle == true) and ((state.pendingSyncCount or 0) > 0)
        syncBtnAbovePet:SetShown(showSync)
    end

    if optPanel and optPanel:IsShown() then
        if optPanel.RefreshControls then
            optPanel:RefreshControls()
        else
            RefreshOptionsCountsText()
        end
    end

    if not state.petName or not state.hasProfile or state.stopped then
        statusDot:SetColorTexture(1, 0.15, 0.15, 1)
    elseif PetRP_Config.paused then
        statusDot:SetColorTexture(1, 0.85, 0.1, 1)
    else
        statusDot:SetColorTexture(0.1, 0.95, 0.2, 1)
    end
end

function PetRP.InitUI()
    RegisterOpenDyslexicFont()
    bar = CreateFrame("Frame", "PetRPBar", UIParent, "BackdropTemplate")
    bar:SetSize(64, 80)
    bar:SetFrameStrata("HIGH")
    local pos = PetRP_Config.pos
    bar:SetPoint(pos.point, UIParent, pos.point, pos.x, pos.y)
    bar:SetScale(PetRP_Config.scale or 1.0)
    bar:SetBackdrop(TRP3_STYLE_BACKDROP)

    -- Top: Active Companion Pet Icon
    local petBtn = CreateFrame("Button", nil, bar)
    petBtn:SetSize(38, 38)
    petBtn:SetPoint("TOP", bar, "TOP", 0, -9)
    petBtn:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square", "ADD")
    MakeDraggable(petBtn)

    petIcon = petBtn:CreateTexture(nil, "ARTWORK")
    petIcon:SetAllPoints()

    statusBorder = petBtn:CreateTexture(nil, "OVERLAY", nil, 1)
    statusBorder:SetSize(10, 10)
    statusBorder:SetPoint("TOPLEFT", petBtn, "TOPLEFT", -2, 2)
    statusBorder:SetColorTexture(0, 0, 0, 1)

    statusDot = petBtn:CreateTexture(nil, "OVERLAY", nil, 2)
    statusDot:SetSize(8, 8)
    statusDot:SetPoint("CENTER", statusBorder, "CENTER", 0, 0)
    statusDot:SetColorTexture(1, 0.15, 0.15, 1)

    petBtn:RegisterForClicks("LeftButtonUp", "RightButtonUp")
    petBtn:SetScript("OnClick", function(self, btn)
        if not state.petName then
            PetRP.SyncCompanionState()
        elseif not state.hasProfile then
            PetRP.OpenTRP3CompanionsDialog()
        elseif btn == "RightButton" then
            PetRP.ToggleOptions()
        else
            PetRP.Speak()
        end
        ShowStatusTooltip(self)
    end)
    petBtn:SetScript("OnEnter", function(self) ShowStatusTooltip(self) end)
    petBtn:SetScript("OnLeave", function() GameTooltip:Hide() end)

    -- Bottom Row (under Pet Icon): Matched TimeManager [Play/Pause] and [Stop] icons
    playPauseBtn = CreateControlBtn(bar, 9, PLAY_ICON, function()
        return (state.stopped or PetRP_Config.paused) and "Click: Play / Resume" or "Click: Pause Timer"
    end, PetRP.TogglePlayPause)

    stopBtn = CreateControlBtn(bar, 33, STOP_ICON, function()
        return state.stopped and "Already Stopped (Click Play to start)" or "Click: Stop Timer"
    end, PetRP.Stop)

    bar:SetScript("OnEnter", function(self) ShowStatusTooltip(self) end)
    bar:SetScript("OnLeave", function() GameTooltip:Hide() end)

    local tooltipElapsed = 0
    bar:SetScript("OnUpdate", function(_, elapsed)
        tooltipElapsed = tooltipElapsed + (elapsed or 0)
        if tooltipElapsed >= 0.5 then
            tooltipElapsed = 0
            if GameTooltip and GameTooltip:IsShown() and GameTooltip:GetOwner() == bar then
                ShowStatusTooltip(bar, lastTooltipActionLabel)
            end
        end
    end)

    CreateSmuggleSyncUI(bar)
    AttachDragAndResize(bar)
    optPanel = CreateOptionsPanel(bar)
    PetRP.UpdateUI()
end
