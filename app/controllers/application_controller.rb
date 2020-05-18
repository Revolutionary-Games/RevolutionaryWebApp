# frozen_string_literal: true

# Main controller with the most general functionality
class ApplicationController < ActionController::Base
  include ApplicationHelper

  def acting_user
    # Returns logged in user
    @acting_user ||= acting_user_from_session(session)
  end
end
