# frozen_string_literal: true

module UserOps
  # Removes a specific launcher link
  class DeleteLauncherLink < Hyperstack::ServerOp
    param :acting_user
    param :link_id
    add_error(:link_id, :does_not_exist, 'link does not exist') {
      !(@link = LauncherLink.find_by_id(params.link_id))
    }
    validate { @link.user == params.acting_user || params.acting_user.admin? }
    step {
      @link.destroy!
    }
  end
end
