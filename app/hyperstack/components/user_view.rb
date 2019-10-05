class UserProperties < HyperComponent
  param :user
  param :showToken, default: false
  param :showLFS, default: false

  render(DIV) do

    lookingAtSelf = @User == App.acting_user

    if lookingAtSelf
      P { "This is you" }
    end

    P { "email: #{ @User.email}" }
    P { "name: #{ @User.name}" }
    P { "admin: #{ @User.admin}" }
    P { "developer: #{ @User.developer}" }
    P { "sso_source: #{ @User.sso_source}" }
    P { "local: #{ @User.local}" }

    hasToken = @User.has_api_token
    hasLFS = @User.has_lfs_token

    P { "api token: #{hasToken}" }

    if hasToken == "true" && lookingAtSelf
      BUTTON { if !@ShowToken then 'View Token' else 'Hide Token' end }.on(:click){
        mutate @ShowToken = !@ShowToken
      }

      if @ShowToken
        P { "Your token is: #{@User.api_token}" }
      end

      BUTTON { "Regenerate Token"}.on(:click){
        GenerateAPITokenForUser.run
          .then { mutate @ShowToken = true }
          .fail { alert "failed to run operation" }
      }

      BUTTON { "Clear Token" }.on(:click){
        ResetAPITokenForUser.run(user_id: @User.id)
          .then { mutate @ShowToken = false }
          .fail { alert "failed to run operation" }
      }

    elsif lookingAtSelf

      BUTTON { "Generate API Token"}.on(:click){
        GenerateAPITokenForUser.run
          .then { mutate @ShowToken = true }
          .fail { alert "failed to run operation" }
      }
    end

    P { "Git lfs token: #{hasLFS}" }

    if hasLFS == "true" && lookingAtSelf
      BUTTON { if !@ShowLFS then 'View LFS Token' else 'Hide LFS Token' end }.on(:click){
        mutate @ShowLFS = !@ShowLFS
      }

      if @ShowLFS
        P { "Your Git LFS token is: #{@User.lfs_token}" }
      end

      BUTTON { "Regenerate Token"}.on(:click){
        GenerateLFSTokenForUser.run
          .then { mutate @ShowLFS = true }
          .fail { alert "failed to run operation" }
      }

      BUTTON { "Clear Token" }.on(:click){
        ResetLFSTokenForUser.run(user_id: @User.id)
          .then { mutate @ShowLFS = false }
          .fail { alert "failed to run operation" }
      }

    elsif lookingAtSelf

      BUTTON { "Generate Git LFS Token"}.on(:click){

        GenerateLFSTokenForUser.run
          .then { mutate @ShowLFS = true }
          .fail { alert "failed to run operation" }
      }
    end

    if !lookingAtSelf
      H2 {"Actions"}
      BUTTON { 'Force Clear Tokens' }.on(:click){
        ResetLFSTokenForUser.run(user_id: @User.id).then {
          ResetAPITokenForUser.run(user_id: @User.id)
        }.then{
          alert "success"
        }.fail{ alert "failed to run operation" }
      }
    end
  end
end

class UserView < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do

    user = User.find_by_id match.params[:id]

    if !user
      H1 { "No user exists with id = #{match.params[:id]}" }
      return
    end

    H1 { "User #{user.email} (#{user.id})" }

    H2 { "Properties" }

    UserProperties(user: user)

  end
end
