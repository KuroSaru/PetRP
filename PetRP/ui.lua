local bar, petIcon, statusDot, statusBorder, playPauseBtn, optPanel, syncBtnAbovePet, syncGlowTex, smuggleBox, countsLabel
local state = PetRP.state

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
    if TRP3_IconUtil and TRP3_IconUtil.SetTextureToIcon then
        TRP3_IconUtil.SetTextureToIcon(tex, iconVal)
    elseif tonumber(iconVal) then
        tex:SetTexture(tonumber(iconVal))
    elseif type(iconVal) == "string" and iconVal ~= "" then
        local path = iconVal:find("\\") and iconVal or ("Interface\\Icons\\" .. iconVal)
        tex:SetTexture(path)
    else
        tex:SetTexture("Interface\\Icons\\ability_hunter_beastcall")
    end
end

local function RefreshOptionsCountsText()
    if not countsLabel then return end
    local nReact, nEmote, nSay, nDefEmote, nDefSay = 0, 0, 0, 30, 30
    if PetRP.GetPoolCounts then
        nReact, nEmote, nSay, nDefEmote, nDefSay = PetRP.GetPoolCounts()
    end
    local totalEmotes = nEmote + nDefEmote
    local totalSays = nSay + nDefSay
    local grandTotal = nReact + totalEmotes + totalSays
    countsLabel:SetText(string.format(
        "|cffffd100Loaded Messages (%d Total):|r\n" ..
        "• Reactive Emotes: |cff00ff00%d|r\n" ..
        "• Periodic Emotes: |cff00ff00%d|r (%d AI + %d Default)\n" ..
        "• Periodic Says: |cff00ff00%d|r (%d AI + %d Default)",
        grandTotal, nReact, totalEmotes, nEmote, nDefEmote, totalSays, nSay, nDefSay))
end

local function ShowStatusTooltip(_, actionLabel)
    if not bar then return end
    GameTooltip:SetOwner(bar, "ANCHOR_TOPLEFT", 0, 6)
    GameTooltip:ClearLines()
    GameTooltip:AddLine("PetRP Companion Bridge", 1, 0.675, 0.125)

    local mode
    if state.stopped then
        mode = "|cffff3333[LED: RED] Stopped|r"
    elseif PetRP_Config.paused then
        mode = "|cffffcc00[LED: YELLOW] Paused|r"
    elseif not state.petName then
        mode = "|cffff3333[LED: RED] Active (No Pet Out)|r"
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
    local nextStatus = string.format("|cff00ff00%d Loaded|r (%s)", nReact, (nextMsg or ""):sub(1, 22))
    local emoteStatus = string.format("|cff00ff00%d Loaded|r [%d AI + %d Def] (%s)", totalEmotes, nEmote, nDefEmote, (perEmote or ""):sub(1, 18))
    local sayStatus = string.format("|cff00ff00%d Loaded|r [%d AI + %d Def] (%s)", totalSays, nSay, nDefSay, (perSay or ""):sub(1, 18))

    local sec = PetRP_Config.interval or 180
    local minPart = math.floor(sec / 60)
    local secPart = sec % 60
    local intervalText = string.format("%d Seconds (%dm %ds)", sec, minPart, secPart)

    GameTooltip:AddDoubleLine("Status:", mode, 1, 1, 1)
    GameTooltip:AddDoubleLine("Active Companion:", state.petName or "|cff888888None Summoned|r", 1, 1, 1)
    GameTooltip:AddDoubleLine("TRP3 RP Name:", state.rpName or "|cff888888Unlinked|r", 1, 1, 1)
    GameTooltip:AddDoubleLine("Periodic Timer:", intervalText, 1, 1, 1)
    GameTooltip:AddDoubleLine("Reactive Emotes:", nextStatus, 1, 1, 1)
    GameTooltip:AddDoubleLine("Periodic Emotes:", emoteStatus, 1, 1, 1)
    GameTooltip:AddDoubleLine("Periodic Says:", sayStatus, 1, 1, 1)

    if state.hasPendingSmuggle then
        GameTooltip:AddDoubleLine("Sync Status:", "|cff00ff99HIGHLIGHTED — Updates Ready! Click Sync Icon Above Pet|r", 1, 0.85, 0)
    else
        GameTooltip:AddDoubleLine("Sync Status:", "|cff888888Up to Date (Click Sync Icon Above Pet to Force Sync)|r", 1, 1, 1)
    end

    if state.persona ~= "" then
        GameTooltip:AddLine(" ")
        GameTooltip:AddLine("TRP3 Persona:", 1, 0.82, 0)
        GameTooltip:AddLine(state.persona, 0.9, 0.9, 0.9, true)
    end
    if actionLabel then
        GameTooltip:AddLine(" ")
        GameTooltip:AddLine(actionLabel, 0.2, 1, 0.6)
    end
    GameTooltip:AddLine("Left-Click (or Shift+Click) Pet: Speak | Right-Click Pet: Options", 0.7, 0.85, 1)
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
    syncBeaconFrame:SetSize(4, 4)
    syncBeaconFrame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
    local syncBeacon = syncBeaconFrame:CreateTexture(nil, "OVERLAY")
    syncBeacon:SetAllPoints(syncBeaconFrame)
    syncBeacon:SetColorTexture(0.078431, 0.968627, 0.454902, 1.0)
    syncBeaconFrame:Hide()

    function PetRP.FlashReloadBeacon()
        syncBeaconFrame:Show()
        C_Timer.After(0.6, function() syncBeaconFrame:Hide() end)
    end

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
    p:SetSize(255, 162)
    p:SetFrameStrata("FULLSCREEN_DIALOG")
    p:SetFrameLevel(200)
    p:SetToplevel(true)
    p:SetClampedToScreen(true)
    p:SetBackdrop(TRP3_STYLE_BACKDROP)
    p:Hide()

    local title = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
    title:SetPoint("TOPLEFT", 12, -12)
    title:SetText("Timer Interval (Seconds):")

    local box = CreateFrame("EditBox", nil, p, "InputBoxTemplate")
    box:SetSize(60, 20)
    box:SetPoint("TOPLEFT", 16, -28)
    box:SetAutoFocus(false)
    box:SetNumeric(true)

    local unitLbl = p:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    unitLbl:SetPoint("LEFT", box, "RIGHT", 6, 0)
    local function UpdateUnitLabel(val)
        local s = math.max(15, tonumber(val) or 180)
        unitLbl:SetText(string.format("sec (%dm %ds)", math.floor(s / 60), s % 60))
    end

    box:SetScript("OnTextChanged", function(self)
        UpdateUnitLabel(self:GetText())
    end)
    box:SetScript("OnEnterPressed", function(self)
        PetRP_Config.interval = math.max(15, tonumber(self:GetText()) or 180)
        UpdateUnitLabel(PetRP_Config.interval)
        PetRP.RestartTicker()
        self:ClearFocus()
    end)

    countsLabel = p:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    countsLabel:SetPoint("TOPLEFT", 12, -54)
    countsLabel:SetJustifyH("LEFT")
    countsLabel:SetWidth(230)

    local resetBtn = CreateFrame("Button", nil, p, "UIPanelButtonTemplate")
    resetBtn:SetSize(105, 22)
    resetBtn:SetPoint("BOTTOMLEFT", 12, 12)
    resetBtn:SetText("Reset Size")
    resetBtn:SetScript("OnClick", function()
        PetRP_Config.scale = 1.0
        parent:SetScale(1.0)
        p:SetScale(1.0)
    end)

    local syncBtn = CreateFrame("Button", nil, p, "UIPanelButtonTemplate")
    syncBtn:SetSize(105, 22)
    syncBtn:SetPoint("BOTTOMRIGHT", -12, 12)
    syncBtn:SetText("Sync Pet")
    syncBtn:SetScript("OnClick", function()
        PetRP.SyncCompanionState()
        RefreshOptionsCountsText()
    end)

    p:SetScript("OnShow", function(self)
        self:SetScale(parent:GetScale())
        PositionOptionsPanel()
        local cur = PetRP_Config.interval or 180
        box:SetText(tostring(cur))
        UpdateUnitLabel(cur)
        RefreshOptionsCountsText()
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
    petIcon:SetDesaturated(state.petName == nil or state.stopped)

    local isPausedOrStopped = state.stopped or PetRP_Config.paused
    if playPauseBtn and playPauseBtn.iconTex then
        playPauseBtn.iconTex:SetTexture(isPausedOrStopped and PLAY_ICON or PAUSE_ICON)
    end

    if syncBtnAbovePet then
        local showSync = (state.hasPendingSmuggle == true) and ((state.pendingSyncCount or 0) > 0)
        syncBtnAbovePet:SetShown(showSync)
    end

    if optPanel and optPanel:IsShown() then
        RefreshOptionsCountsText()
    end

    if state.stopped or not state.petName then
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
        if btn == "RightButton" then
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

    CreateControlBtn(bar, 33, STOP_ICON, function()
        return "Click: Stop & Clear Queues"
    end, PetRP.Stop)

    bar:SetScript("OnEnter", function(self) ShowStatusTooltip(self) end)
    bar:SetScript("OnLeave", function() GameTooltip:Hide() end)

    CreateSmuggleSyncUI(bar)
    AttachDragAndResize(bar)
    optPanel = CreateOptionsPanel(bar)
    PetRP.UpdateUI()
end
