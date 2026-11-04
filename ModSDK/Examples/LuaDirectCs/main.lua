local speech = require('scripts.speech')
local M = {}

function M.OnContentReady(mod)
    local function onPlayerEntered(player)
        if player ~= nil and player.IsLocalProfile then
            speech.say(player, '今天想去探索哪里呢？', mod.ModId)
        end
    end

    CS.GameManager.Event_PlayerEnterWorld('+', onPlayerEntered)
    mod:RegisterCleanup(function()
        CS.GameManager.Event_PlayerEnterWorld('-', onPlayerEntered)
    end)
end

return M
