# frozen_string_literal: true

class DebugSymbolPolicy
  # Just send everything to developers
  regulate_broadcast do |policy|
    policy.send_all.to(DeveloperUser)
  end
end
