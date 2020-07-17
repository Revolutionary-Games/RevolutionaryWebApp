# frozen_string_literal: true

# DevBuilds to logged in users
class DevBuildPolicy
  regulate_broadcast { |policy|
    policy.send_all.to(User)
  }
end

