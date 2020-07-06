# frozen_string_literal: true

module AdminOps
  # Deletes the patreon settings
  class DeletePatreonSettings < Hyperstack::ServerOp
    param :acting_user
    param :patreon_settings_id
    add_error(:patreon_settings_id, :does_not_exist, 'patreon settings do not exist') {
      !(@settings = PatreonSettings.find_by_id(params.patreon_settings_id))
    }
    validate { params.acting_user.admin? }
    step {
      @settings.destroy
    }
  end
end
