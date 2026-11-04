local M = {}

function M.say(player, text, topic)
    local controller = player:GetComponentInChildren(
        typeof(CS.FlatWorld.Dialogue.CharacterSoliloquyController), true)
    if controller == nil then return false end

    return controller:Say(text,
        CS.FlatWorld.Dialogue.CharacterSpeechPriority.Player, 5.0, topic)
end

return M
