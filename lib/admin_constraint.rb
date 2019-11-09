# frozen_string_literal: true

# Checks that user is admin before allowing acces to a route
class AdminConstraint
  def matches?(request)
    return false unless request.session[:current_user_id]

    user = User.find request.session[:current_user_id]
    user&.admin?
  end
end
