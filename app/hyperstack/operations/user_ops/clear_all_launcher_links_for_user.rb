# frozen_string_literal: true

module UserOps
  # Removes all launcher links from a user
  class ClearAllLauncherLinksForUser < Hyperstack::ServerOp
    param :acting_user
    param :user_id
    add_error(:user_id, :does_not_exist, 'user does not exist') {
      !(@user = User.find_by_id(params.user_id))
    }
    validate { @user == params.acting_user || params.acting_user.admin? }
    step {
      @user.launcher_links.each(&:destroy)
      @user.launcher_code_expires = Time.now
      @user.launcher_link_code = nil
      @user.save!
    }
  end
end
