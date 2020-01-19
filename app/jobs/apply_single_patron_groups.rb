# frozen_string_literal: true

# Makes sure a single user has the right forum groups set based on patron data
class ApplySinglePatronGroups < ApplicationJob
  queue_as :default

  def apply_adds_and_removes
    PatreonGroupHelper.apply_adds_and_removes @users_to_add_to_devbuilds,
                                              @users_to_remove_from_devbuilds,
                                              @users_to_add_to_vip,
                                              @users_to_remove_from_vip
  end

  def perform(email)
    unless ENV['COMMUNITY_DISCOURSE_API_KEY']
      raise 'COMMUNITY_DISCOURSE_API_KEY env variable is missing'
    end

    # Doesn't exactly work great with multiple settings
    @patreon_settings = PatreonSettings.first

    patron = Patron.find_by email: email

    corresponding_forum_user = if patron
                                 CommunityForumGroups.find_user_by_email patron.alias_or_email
                               else
                                 CommunityForumGroups.find_user_by_email email
                               end

    unless corresponding_forum_user
      puts 'Single user has no forum account, no handling needed for groups'
      # TODO: maybe requeueing a check in 15 minutes should be done once
      return
    end

    username = corresponding_forum_user['username']

    puts "Applying forum groups for single patron (#{username})"

    forum_user = CommunityForumGroups.user_info_by_name username

    unless forum_user
      raise 'Failed to find user group info after finding user object on the forums'
    end

    should_be_group = PatreonGroupHelper.should_be_group_for_patron patron, @patreon_settings
    puts "Target group: #{should_be_group}"

    groups = PatreonGroupHelper.forum_user_relevant_groups forum_user

    puts "User existing groups: #{groups}"

    @users_to_add_to_devbuilds = []
    @users_to_remove_from_devbuilds = []

    @users_to_add_to_vip = []
    @users_to_remove_from_vip = []

    devbuild_owners = PatreonGroupHelper.devbuild_group_owners
    vip_owners = PatreonGroupHelper.vip_group_owners

    # Check what to do with the user
    # Removes
    if should_be_group != :devbuild && groups.include?(:devbuild) &&
       !devbuild_owners.include?(username)
      @users_to_remove_from_devbuilds.push username
    end

    if should_be_group != :vip && groups.include?(:vip) &&
       !vip_owners.include?(username)
      @users_to_remove_from_vip.push username
    end

    # Adds
    if should_be_group == :devbuild && !groups.include?(:devbuild)
      @users_to_add_to_devbuilds.push username
    end

    @users_to_add_to_vip.push username if should_be_group == :vip && !groups.include?(:vip)

    # Apply the changes
    puts 'devbuild add: ', @users_to_add_to_devbuilds, 'devbuild remove:',
         @users_to_remove_from_devbuilds, 'vip add:', @users_to_add_to_vip,
         'vip remove:', @users_to_remove_from_vip

    apply_adds_and_removes
  end
end
