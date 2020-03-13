# frozen_string_literal: true

# Allows developers to see details about LfsObjects
class LfsObjectPolicy
  regulate_broadcast do |policy|
    policy.send_all.to(DeveloperUser)
  end
end
