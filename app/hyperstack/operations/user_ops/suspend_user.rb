# frozen_string_literal: true

module UserOps
  class SuspendUser < Hyperstack::ServerOp
    param :acting_user
    param :user_id
    param :reason
    add_error(:user_id, :does_not_exist, 'user does not exist') {
      !(@user = User.find_by_id(params.user_id))
    }
    add_error(:reason, :cant_be_blank, 'reason is blank') { params.reason.blank? }
    validate { params.acting_user.admin? }
    # Can't edit own suspend status
    validate { params.acting_user != @user }
    step {
      @user.suspended = true
      @user.suspended_manually = true
      @user.suspended_reason = params.reason.strip
      @user.save!
    }
  end
end
