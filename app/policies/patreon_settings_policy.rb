# frozen_string_literal: true

# Allow admins to receive patreon settings info and update the records
class PatreonSettingsPolicy
  # Admins are allowed to update the entry
  allow_update { acting_user.admin? }

  # Admins get all the stuff
  regulate_broadcast { |policy|
    policy.send_all.to(AdminUser)
  }
end
