# frozen_string_literal: true

# Main helper module
module ApplicationHelper
  def self.acting_user_from_session(session)
    user = session[:current_user_id] && User.find_by_id(session[:current_user_id])

    # Session disallowed if suspended
    return nil if user&.suspended?

    user
  end

  def acting_user_from_session(session)
    ApplicationHelper.acting_user_from_session session
  end
end
