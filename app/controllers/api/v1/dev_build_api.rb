# frozen_string_literal: true

module API
  module V1
    # Devbuild API
    class DevBuildAPI < Grape::API
      include API::V1::Defaults

      MAX_OFFER_OBJECTS_BATCH = 100

      helpers do
        def access_key
          code = headers['X-Access-Code']

          return nil if code.blank?

          key = AccessKey.find_by key_code: code, key_type: KEY_TYPE_DEVBUILDS

          error!({ error_code: 401, message: 'Invalid access key' }, 401) unless key

          key.update_last_used

          key
        end
      end

      resource :devbuild do
        desc 'Checks if the server wants the specified devbuild'
        params do
          requires :build_hash, type: String, desc: 'Devbuild hash'
          requires :build_platform, type: String, desc: 'Devbuild platform'
        end
        post 'offer_devbuild' do
          key = access_key
          anonymous = key.nil?

          if anonymous
            upload = DevBuild.find_by(build_hash: permitted_params[:build_hash],
                                      platform: permitted_params[:build_platform]).nil?
          else
            upload = DevBuild.find_by(build_hash: permitted_params[:build_hash],
                                      platform: permitted_params[:build_platform],
                                      anonymous: false).nil?
          end

          status 200
          { upload: upload }
        end

        desc 'Checks if the server wants any of the specified dehydrated objects'
        params do
          requires :objects, type: Array, desc: 'Offered objects (with sha3 and size keys)'
        end
        post 'offer_objects' do
          if permitted_params[:objects].size > MAX_OFFER_OBJECTS_BATCH
            error!(
              {
                error_code: 400,
                message: "Too many objects, max batch size is: #{MAX_OFFER_OBJECTS_BATCH}"
              }, 400
            )
          end

          access_key

          missing_objects = []

          permitted_params[:objects].each { |obj|
            unless DehydratedObject.exists?(sha3: obj['sha3'])
              missing_objects.append obj['sha3']
            end
          }

          status 200
          { upload: missing_objects }
        end

        desc 'Starts upload of a devbuild. The required objects need to be already uploaded'
        params do
          requires :build_hash, type: String, desc: 'Devbuild hash'
          requires :build_branch, type: String, desc: 'Devbuild branch'
          requires :build_platform, type: String, desc: 'Devbuild platform'
          requires :build_size, type: Integer, desc: 'Size of the build'
          requires :required_objects, type: Array, desc:
            'List of the objects needed by this build (just hash)'
        end
        post 'offer_devbuild' do
          key = access_key
          anonymous = key.nil?

          # Check that all objects exist
          permitted_params[:required_objects].each { |sha3|
            next if DehydratedObject.find_by(sha3: sha3).uploaded?

            error!(
              {
                error_code: 400,
                message: "Specified dehydrated object is not uploaded: #{sha3}"
              }, 400
            )
          }

          existing = DevBuild.find_by(build_hash: permitted_params[:build_hash],
                                      platform: permitted_params[:build_platform])

          if anonymous
            if existing
              error!(
                { error_code: 403,
                  message: "Can't upload over an existing devbuild without an access key" },
                403
              )
            end
          else
            # Non-anonymous upload can overwrite an anonymous upload
            if existing&.anonymous
              Rails.logger.info "Anonymous devbuild (#{existing.build_hash}) upload is " \
                                'being overwritten'
              existing.anonymous = false
              existing.save!
            else
              error!(
                {
                  message: "Can't upload a new version of an existing build"
                }, 200
              )
            end
          end

          folder = StorageItem.devbuild_builds_folder

          existing ||= DevBuild.create! build_hash: permitted_params[:build_hash],
                                        platform: permitted_params[:build_platform],
                                        branch: permitted_params[:build_branch],
                                        anonymous: anonymous,
                                        important: !anonymous &&
                                                   permitted_params[:build_branch] == 'master'

          # Apply objects
          permitted_params[:required_objects].each { |sha3|
            dehydrated = DehydratedObject.find_by sha3: sha3

            begin
              existing.dehydrated_objects << dehydrated
            rescue ActiveRecord::RecordNotUnique
              # This happens when the object is already added
            end
          }

          Rails.logger.info "Upload of (#{existing.build_hash} on #{existing.platform}) " \
                            "starting from #{request.ip}"

          # Create storage file to store it
          existing.storage_item ||= StorageItem.create!(
            name: "#{existing.build_hash}_#{existing.platform}.7z", parent: folder,
            ftype: 0, special: true, read_access: ITEM_ACCESS_USER,
            write_access: ITEM_ACCESS_NOBODY
          )
          existing.save!

          # Upload a version of it
          version = existing.storage_item.next_version
          expires = Time.now + RemoteStorageHelper.upload_expire_time
          file = version.create_storage_item(expires, permitted_params[:build_size])

          status 200
          { upload_url: RemoteStorageHelper.create_put_url(file.storage_path),
            verify_token: RemoteStorageHelper.create_put_token(file.storage_path,
                                                               file.size, file.id) }
        end

        desc 'Starts upload of the specified objects'
        params do
          requires :objects, type: Array, desc: 'Objects to upload (with sha3 and size keys)'
        end
        post 'upload_objects' do
          key = access_key

          if permitted_params[:objects].size > MAX_OFFER_OBJECTS_BATCH
            error!(
              {
                error_code: 400,
                message: "Too many objects, max batch size is: #{MAX_OFFER_OBJECTS_BATCH}"
              }, 400
            )
          end

          uploads = []

          folder = StorageItem.dehydrated_folder

          expires = Time.now + RemoteStorageHelper.upload_expire_time

          permitted_params[:required_objects].each { |obj|
            sha3 = obj['sha3']
            size = obj['size']
            dehydrated = DehydratedObject.find_by sha3: sha3

            if dehydrated&.uploaded?
              # Already uploaded
            else
              # Can upload this one
              dehydrated ||= DehydratedObject.create! sha3: sha3

              dehydrated.storage_item ||= StorageItem.create!(
                name: "#{sha3}.gz", parent: folder,
                ftype: 0, special: true, read_access: ITEM_ACCESS_USER,
                write_access: ITEM_ACCESS_NOBODY
              )
              dehydrated.save!

              version = dehydrated.storage_item.next_version
              file = version.create_storage_item(expires, size)

              Rails.logger.info "Upload of dehydrated #{sha3} " \
                                "starting from #{request.ip}"

              uploads.append(
                {
                  sha3: sha3,
                  upload_url: RemoteStorageHelper.create_put_url(file.storage_path),
                  verify_token: RemoteStorageHelper.create_put_token(file.storage_path,
                                                                     file.size, file.id)
                }
              )
            end
          }

          status 200
          { upload: uploads }
        end

        desc 'Report finished devbuild / object upload'
        params do
          requires :token, type: String, desc: "Finished upload's token"
        end
        post 'finish' do
          begin
            data, item, version = RemoteStorageHelper.handle_finished_token(
              permitted_params[:token]
            )
          rescue StandardError => e
            error!({ error_code: 400, message: e.to_s }, 400)
            return
          end

          Rails.logger.info "DevBuild item (#{item.storage_path}) is now uploaded"

          item.uploading = false
          version.uploading = false
          version.storage_item.size = item.size
          item.save!
          version.save!
          version.storage_item.save!

          # TODO: does something else need to be done?

          status 200
        end
      end
    end
  end
end
