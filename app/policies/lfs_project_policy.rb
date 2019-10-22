# frozen_string_literal: true

class LfsProjectPolicy
  regulate_broadcast do |policy|
    policy.send_all.to(DeveloperUser)

    if public
      policy.send_all.to(
        Hyperstack::Application
      )
    else
      policy.send_only(:public).to(Hyperstack::Application)
    end
  end
end
