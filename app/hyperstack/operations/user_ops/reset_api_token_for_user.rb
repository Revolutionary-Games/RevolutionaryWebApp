# frozen_string_literal: true

module UserOps
  class ResetAPITokenForUser < Hyperstack::ServerOp
    param :acting_user
    param :user_id
    add_error(:user_id, :does_not_exist, 'user does not exist') {
      !(@user = User.find_by_id(params.user_id))
    }
    validate { @user == params.acting_user || params.acting_user.admin? }
    step {
      @user.reset_api_token
      @user.save!
    }
  end
end
