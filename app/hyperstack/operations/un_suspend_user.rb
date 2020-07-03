# frozen_string_literal: true

class UnSuspendUser < Hyperstack::ServerOp
  param :acting_user
  param :user_id
  add_error(:user_id, :does_not_exist, 'user does not exist') {
    !(@user = User.find_by_id(params.user_id))
  }
  validate { params.acting_user.admin? }
  # Can't edit own suspend status
  validate { params.acting_user != @user }
  step {
    @user.suspended = false

    @user.suspended_manually = false
    @user.save!
  }
end
