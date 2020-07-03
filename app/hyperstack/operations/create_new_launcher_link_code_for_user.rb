# frozen_string_literal: true

DEFAULT_MAX_LAUNCHER_LINKS = 5

# Sets up a new launcher link code for the target user
class CreateNewLauncherLinkCodeForUser < Hyperstack::ServerOp
  param :acting_user
  param :user_id
  add_error(:user_id, :does_not_exist, 'user does not exist') {
    !(@user = User.find_by_id(params.user_id))
  }
  validate { @user == params.acting_user }
  step {
    existing = @user.launcher_links.count
    if existing >= DEFAULT_MAX_LAUNCHER_LINKS
      raise "This account already has #{existing} links, " \
            'which is at or above the limit'
    end
  }
  step {
    @user.launcher_code_expires = Time.now + 15.minutes
    @user.launcher_link_code = SecureRandom.base58(32)
    @user.save!
  }
end
