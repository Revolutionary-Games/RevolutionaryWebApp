class ApplicationController < ActionController::Base
  def acting_user
    # Returns logged in user
    @acting_user ||= session[:current_user_id] && User.find_by_id(session[:current_user_id])
  end
end
