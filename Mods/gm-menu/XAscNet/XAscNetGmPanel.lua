-- Pure-Lua replacement for the stripped XDebugManager GM panel.
local Panel = {}
local U = CS.UnityEngine
local UI = U.UI
local labels = require("XAscNet/XAscNetGmLabels")
local root, body, title, errorText, font, current, building
local stack = {}
local initialized, watching = false, false
local init
local white = U.Color(1, 1, 1, 1)
local function translate(value)
    value = tostring(value or "")
    return labels[value] or value
end
local function report(message)
    message = tostring(message)
    if errorText then errorText.text = message end
    local logger = rawget(_G, "XLog")
    if logger and logger.Error then pcall(logger.Error, "[GM menu] " .. message) end
end
local function safe(callback, ...)
    if not callback then return true end
    local args = table.pack(...)
    local ok, result = xpcall(function() return callback(table.unpack(args, 1, args.n)) end, debug.traceback)
    if not ok then report(result) end
    return ok, result
end
local function listen(event, callback)
    event:AddListener(function(...) safe(callback, ...) end)
end
local function object(name, parent)
    local go = U.GameObject(name)
    local rect = go:AddComponent(typeof(U.RectTransform))
    if parent then rect:SetParent(parent, false) end
    return go, rect
end
local function stretch(rect, left, bottom, right, top)
    rect.anchorMin = U.Vector2(0, 0)
    rect.anchorMax = U.Vector2(1, 1)
    rect.offsetMin = U.Vector2(left or 0, bottom or 0)
    rect.offsetMax = U.Vector2(-(right or 0), -(top or 0))
end
local function image(go, color)
    local component = go:AddComponent(typeof(UI.Image))
    component.color = color
    return component
end
local function text(parent, name, value, size)
    local go, rect = object(name, parent)
    stretch(rect, 12, 4, 12, 4)
    local component = go:AddComponent(typeof(UI.Text))
    component.font = font
    component.fontSize = size or 23
    component.color = white
    component.alignment = U.TextAnchor.MiddleLeft
    component.horizontalOverflow = U.HorizontalWrapMode.Wrap
    component.verticalOverflow = U.VerticalWrapMode.Truncate
    component.raycastTarget = false
    component.text = translate(value)
    return component, rect
end
local function button(parent, name, callback)
    local go, rect = object(name, parent)
    local background = image(go, U.Color(0.16, 0.20, 0.27, 1))
    local component = go:AddComponent(typeof(UI.Button))
    component.targetGraphic = background
    text(rect, "TxtName", name)
    listen(component.onClick, callback)
    return component, rect
end
local function page(name, parent)
    local go, rect = object(name, body)
    stretch(rect)
    local layout = go:AddComponent(typeof(UI.VerticalLayoutGroup))
    layout.spacing = 7
    layout.childControlWidth = true
    layout.childControlHeight = true
    layout.childForceExpandWidth = true
    layout.childForceExpandHeight = false
    local fitter = go:AddComponent(typeof(UI.ContentSizeFitter))
    fitter.verticalFit = UI.ContentSizeFitter.FitMode.PreferredSize
    rect.anchorMin = U.Vector2(0, 1)
    rect.anchorMax = U.Vector2(1, 1)
    rect.pivot = U.Vector2(0.5, 1)
    rect.anchoredPosition = U.Vector2(0, 0)
    local result = {go = go, rect = rect, name = name, parent = parent, refreshers = {}}
    go:SetActive(false)
    return result
end
local function row(go, height)
    local layout = go:AddComponent(typeof(UI.LayoutElement))
    layout.preferredHeight = height or 58
    layout.minHeight = height or 58
end
local scroll
local function refresh(level)
    for _, entry in ipairs(level.refreshers) do
        safe(entry.callback, entry.text)
        local value = entry.text.text
        for _, prefix in ipairs({"当前服务器", "已启动时间", "用户名", "客户端版本", "原版本", "现版本", "未登录"}) do
            value = value:gsub(prefix, function() return translate(prefix) end)
        end
        entry.text.text = value
    end
end
local function show(level)
    if current then current.go:SetActive(false) end
    current = level
    level.go:SetActive(true)
    scroll.content = level.rect
    scroll.verticalNormalizedPosition = 1
    local names, cursor = {}, level
    while cursor do table.insert(names, 1, cursor.name); cursor = cursor.parent end
    title.text = table.concat(names, " / ")
    refresh(level)
end
local function create()
    local texts = U.Resources.FindObjectsOfTypeAll(typeof(UI.Text))
    for i = 0, texts.Length - 1 do
        if texts[i].font then font = texts[i].font; break end
    end
    assert(font, "No game UI font available yet; open the menu after the login screen appears")
    local go, rect = object("AscNetGmMenu")
    root = go
    U.Object.DontDestroyOnLoad(root)
    local canvas = go:AddComponent(typeof(U.Canvas))
    canvas.renderMode = U.RenderMode.ScreenSpaceOverlay
    canvas.sortingOrder = 32760
    local scaler = go:AddComponent(typeof(UI.CanvasScaler))
    scaler.uiScaleMode = UI.CanvasScaler.ScaleMode.ScaleWithScreenSize
    scaler.referenceResolution = U.Vector2(1920, 1080)
    scaler.matchWidthOrHeight = 0.5
    go:AddComponent(typeof(UI.GraphicRaycaster))
    local window, windowRect = object("Window", rect)
    windowRect.anchorMin = U.Vector2(0.5, 0.5)
    windowRect.anchorMax = U.Vector2(0.5, 0.5)
    windowRect.sizeDelta = U.Vector2(1180, 920)
    image(window, U.Color(0.035, 0.045, 0.065, 0.98))
    local titleRect
    title, titleRect = text(windowRect, "Path", "GM", 25)
    titleRect.anchorMin = U.Vector2(0, 1); titleRect.anchorMax = U.Vector2(1, 1)
    titleRect.offsetMin = U.Vector2(20, -64); titleRect.offsetMax = U.Vector2(-240, -8)
    local _, backRect = button(windowRect, "Back", function()
        if #stack > 0 then show(table.remove(stack)) end
    end)
    backRect.anchorMin = U.Vector2(1, 1); backRect.anchorMax = U.Vector2(1, 1)
    backRect.offsetMin = U.Vector2(-230, -58); backRect.offsetMax = U.Vector2(-125, -8)
    local _, closeRect = button(windowRect, "Close", function() root:SetActive(false) end)
    closeRect.anchorMin = U.Vector2(1, 1); closeRect.anchorMax = U.Vector2(1, 1)
    closeRect.offsetMin = U.Vector2(-115, -58); closeRect.offsetMax = U.Vector2(-10, -8)
    local viewport, viewportRect = object("Viewport", windowRect)
    stretch(viewportRect, 16, 100, 16, 76)
    image(viewport, U.Color(0.06, 0.07, 0.09, 1))
    viewport:AddComponent(typeof(UI.RectMask2D))
    body = viewportRect
    scroll = viewport:AddComponent(typeof(UI.ScrollRect))
    scroll.viewport = viewportRect
    scroll.horizontal = false
    scroll.movementType = UI.ScrollRect.MovementType.Clamped
    local errorRect
    errorText, errorRect = text(windowRect, "Error", "", 18)
    errorText.color = U.Color(1, 0.25, 0.25, 1)
    errorRect.anchorMin = U.Vector2(0, 0); errorRect.anchorMax = U.Vector2(1, 0)
    errorRect.offsetMin = U.Vector2(16, 8); errorRect.offsetMax = U.Vector2(-16, 92)
    current = page("GM")
    building = current
end
-- The shipped optional column span is irrelevant to this single-column layout.
function Panel:AddButton(name, callback, span)
    local component = button((building or current).rect, translate(name), callback)
    row(component.gameObject)
    return component
end
function Panel:AddToggle(name, callback)
    local go, rect = object(translate(name), (building or current).rect)
    row(go)
    local background = image(go, U.Color(0.13, 0.17, 0.23, 1))
    local component = go:AddComponent(typeof(UI.Toggle))
    component.targetGraphic = background
    local check, checkRect = object("Check", rect)
    checkRect.anchorMin = U.Vector2(0, 0.5); checkRect.anchorMax = U.Vector2(0, 0.5)
    checkRect.sizeDelta = U.Vector2(24, 24); checkRect.anchoredPosition = U.Vector2(25, 0)
    component.graphic = image(check, U.Color(0.2, 0.95, 0.65, 1))
    local _, labelRect = text(rect, "TxtName", name)
    labelRect.offsetMin = U.Vector2(50, 4)
    listen(component.onValueChanged, callback)
    return component
end
function Panel:AddInput(name, callback, numeric)
    local go, rect = object(translate(name), (building or current).rect)
    row(go, 78)
    image(go, U.Color(0.1, 0.13, 0.18, 1))
    local label, labelRect = text(rect, "Label", name, 19)
    labelRect.anchorMin = U.Vector2(0, 0.5)
    local value, valueRect = text(rect, "Value", "", 23)
    valueRect.anchorMax = U.Vector2(1, 0.5)
    local placeholder = text(rect, "Placeholder", "Enter value", 20)
    stretch(placeholder.rectTransform, 12, 4, 12, 4)
    placeholder.rectTransform.anchorMax = U.Vector2(1, 0.5)
    placeholder.color = U.Color(0.55, 0.6, 0.65, 1)
    local component = go:AddComponent(typeof(UI.InputField))
    component.textComponent = value
    component.placeholder = placeholder
    component.targetGraphic = go:GetComponent(typeof(UI.Image))
    if numeric then component.contentType = UI.InputField.ContentType.IntegerNumber end
    -- GM inputs are ids, IPs and module names: surrounding whitespace is never meaningful and breaks lookups.
    listen(component.onEndEdit, function(text, ...)
        if type(text) == "string" then text = text:match("^%s*(.-)%s*$") end
        return callback(text, ...)
    end)
    return component
end
function Panel:AddText(name, refresher)
    local component = text((building or current).rect, "Text", name)
    row(component.gameObject, 80)
    if refresher then table.insert((building or current).refreshers, {text = component, callback = refresher}) end
    return component
end
function Panel:AddSubMenu(name, builder, rebuildOnOpen)
    local parent = building or current
    local level
    return self:AddButton(name, function()
        if not level then level = page(translate(name), parent) end
        if not level.built or rebuildOnOpen then
            for i = level.rect.childCount - 1, 0, -1 do
                local child = level.rect:GetChild(i)
                child.gameObject:SetActive(false)
                U.Object.Destroy(child.gameObject)
            end
            level.refreshers = {}
            building = level
            local ok = safe(builder)
            building = nil
            level.built = ok
        end
        table.insert(stack, parent)
        show(level)
    end)
end
function Panel.Toggle()
    if root and root.activeSelf then root:SetActive(false); return end
    if not root then
        local ok = safe(create)
        if not ok then
            if root then U.Object.Destroy(root) end
            root, current, building, errorText = nil, nil, nil, nil
            return
        end
    end
    root:SetActive(true)
    if not initialized then
        initialized = true
        building = current
        safe(init)
        safe(Panel.AddAscNetMenu)
        building = nil
    end
    show(current)
end
-- AscNet tools next to the game's own menus. Hot reload uses the game's XHotReload (XHotReload.lua:110), which
-- needs remote-config Debug and re-reads the module through the external loader; UI modules apply on next open.
function Panel.AddAscNetMenu()
    local module = "XUi/XUiLogin/XUiLogin"
    Panel:AddSubMenu("AscNet", function()
        Panel:AddInput("Lua module (e.g. XUi/XUiLogin/XUiLogin)", function(value) module = value end)
        Panel:AddButton("Hot reload module", function()
            local reloader = rawget(_G, "XHotReload")
            if not (reloader and reloader.Reload) then error("XHotReload is not loaded (needs remote-config Debug)") end
            if not package.loaded[module] then error("not loaded yet, nothing to reload: " .. tostring(module)) end
            reloader.Reload(module)
            XUiManager.TipMsg("Reloaded " .. module)
        end)
    end)
end
function Panel.StartWatcher(callback)
    if watching then return end
    init = callback
    Panel.ClaimSkipId()
    local scheduler = rawget(_G, "XScheduleManager")
    -- Runs while the game is still requiring its managers: never throw here, or the boot require fails.
    if not (scheduler and scheduler.ScheduleForever) then
        XLog.Error("AscNet GM menu: XScheduleManager is not loaded; Ctrl+Shift+D is unavailable")
        return
    end
    watching = true
    local function poll()
        local input, key = U.Input, U.KeyCode
        if input.GetKeyDown(key.D) and (input.GetKey(key.LeftControl) or input.GetKey(key.RightControl))
            and (input.GetKey(key.LeftShift) or input.GetKey(key.RightShift)) then Panel.Toggle() end
    end
    local function arm()
        scheduler.ScheduleForever(function() safe(poll) end, 0)
    end
    -- Boot and every return to login clear timers, including this early registration.
    local private = require("XCommon/XScheduleManager")
    local clear = private.__Clear__
    private.__Clear__ = function(...)
        clear(...)
        safe(arm)
    end
    arm()
end

-- Main-screen second menu ("GM Menu" next to Official Site / Swap Team). The grid's system entries come from
-- XUiConfigs.GetSystemSubMenuList() (Client/Ui/UiMainSubMenu.tab), filtered by XFunctionManager.CheckInTimeByTimeId:
-- a row without a server-sent, currently open TimeId is hidden (XFunctionManager.lua:742), and AscNet's time ids follow
-- its activity schedule. So the entry is appended to the filtered list here instead of added as a table row.
-- Clicking it calls XFunctionManager.SkipInterface(SKIP_ID); that id is unused in the shipped SkipFunctional.tab
-- (4.8: 3434 rows, highest 11080106), so no shipped skip is shadowed.
Panel.SKIP_ID = 990001
-- Every XTableUiMainSubMenu column: consumers rely on the loader's row shape, where list columns are empty tables
-- (XRedPointConditionSubMenuNewSystem iterates RedPointCondition with ipairs; nil there broke the whole main screen).
local SUB_MENU_ENTRY = {
    Id = 990001, TimeId = 0, Title = "GM Menu", SubTitle = "AscNet", Desc = "AscNet", StyleType = 2,
    BtnIcon = "Assets/Product/Texture/Atlas/UiMain/UiMainIconLogoStrategy.png", SkipId = Panel.SKIP_ID,
    ConditionId = 0, RedPointCondition = {}, RedPointParam = {},
}
function Panel.ClaimSkipId()
    local functions = rawget(_G, "XFunctionManager")
    local configs = rawget(_G, "XUiConfigs")
    if not (functions and type(functions.SkipInterface) == "function"
        and configs and type(configs.GetSystemSubMenuList) == "function") then
        XLog.Error("AscNet GM menu: XFunctionManager/XUiConfigs not loaded; no second-menu button")
        return
    end
    local skip = functions.SkipInterface
    functions.SkipInterface = function(id, ...)
        if id == Panel.SKIP_ID then
            if not (root and root.activeSelf) then Panel.Toggle() end
            return true
        end
        return skip(id, ...)
    end
    local list = configs.GetSystemSubMenuList
    configs.GetSystemSubMenuList = function(...)
        local entries = list(...)
        table.insert(entries, SUB_MENU_ENTRY)
        return entries
    end
end
return Panel
